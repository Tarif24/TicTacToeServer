using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using System.Collections.Generic;
using System.IO;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 54321;

    const int MaxNumberOfClientConnections = 1000;

    List<string> usernames;
    List<string> passwords;

    const string FilePathForSavedLoginInfo = "SaveLoginInformation.txt";

    void Start()
    {
        networkDriver = NetworkDriver.Create();
        reliableAndInOrderPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        nonReliableNotInOrderedPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage));
        NetworkEndpoint endpoint = NetworkEndpoint.AnyIpv4;
        endpoint.Port = NetworkPort;

        int error = networkDriver.Bind(endpoint);
        if (error != 0)
            Debug.Log("Failed to bind to port " + NetworkPort);
        else
            networkDriver.Listen();

        networkConnections = new NativeList<NetworkConnection>(MaxNumberOfClientConnections, Allocator.Persistent);

        usernames = new List<string>();
        passwords = new List<string>();

        if (File.Exists(FilePathForSavedLoginInfo))
        {
            LoadLoginInfo();
        }
    }

    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {
        #region Check Input and Send Msg

        if (Input.GetKeyDown(KeyCode.A))
        {
            for (int i = 0; i < networkConnections.Length; i++)
            {
                SendMessageToClient("Hello client's world, sincerely your network server", networkConnections[i]);
            }
        }

        #endregion

        networkDriver.ScheduleUpdate().Complete();

        #region Remove Unused Connections

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
            {
                networkConnections.RemoveAtSwapBack(i);
                i--;
            }
        }

        #endregion

        #region Accept New Connections

        while (AcceptIncomingConnection())
        {
            Debug.Log("Accepted a client connection");
        }

        #endregion

        #region Manage Network Events

        DataStreamReader streamReader;
        NetworkPipeline pipelineUsedToSendEvent;
        NetworkEvent.Type networkEventType;

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
                continue;

            while (PopNetworkEventAndCheckForData(networkConnections[i], out networkEventType, out streamReader, out pipelineUsedToSendEvent))
            {
                if (pipelineUsedToSendEvent == reliableAndInOrderPipeline)
                    Debug.Log("Network event from: reliableAndInOrderPipeline");
                else if (pipelineUsedToSendEvent == nonReliableNotInOrderedPipeline)
                    Debug.Log("Network event from: nonReliableNotInOrderedPipeline");

                switch (networkEventType)
                {
                    case NetworkEvent.Type.Data:
                        int dataSignifier = streamReader.ReadInt();

                        int sizeOfDataBuffer = streamReader.ReadInt();
                        NativeArray<byte> buffer = new NativeArray<byte>(sizeOfDataBuffer, Allocator.Persistent);
                        streamReader.ReadBytes(buffer);
                        byte[] byteBuffer = buffer.ToArray();
                        string msg = Encoding.Unicode.GetString(byteBuffer);
                        
                        string[] loginInfo = msg.Split(',');

                        if (dataSignifier == DataSignifiers.AccountSignup)
                        {
                            ProcessUserSignup(loginInfo[0], loginInfo[1], networkConnections[i]);
                        }
                        else if (dataSignifier == DataSignifiers.AccountSignin)
                        {
                            ProcessUserSignin(loginInfo[0], loginInfo[1], networkConnections[i]);
                        }
                        else if (dataSignifier == DataSignifiers.Message)
                        {
                            ProcessReceivedMsg(msg);
                        }

                        buffer.Dispose();
                        break;
                    case NetworkEvent.Type.Disconnect:
                        Debug.Log("Client has disconnected from server");
                        networkConnections[i] = default(NetworkConnection);
                        break;
                }
            }
        }

        #endregion
    }

    private bool AcceptIncomingConnection()
    {
        NetworkConnection connection = networkDriver.Accept();
        if (connection == default(NetworkConnection))
            return false;

        networkConnections.Add(connection);
        return true;
    }

    private bool PopNetworkEventAndCheckForData(NetworkConnection networkConnection, out NetworkEvent.Type networkEventType, out DataStreamReader streamReader, out NetworkPipeline pipelineUsedToSendEvent)
    {
        networkEventType = networkConnection.PopEvent(networkDriver, out streamReader, out pipelineUsedToSendEvent);

        if (networkEventType == NetworkEvent.Type.Empty)
            return false;
        return true;
    }

    private void ProcessReceivedMsg(string msg)
    {
        Debug.Log("Msg received = " + msg);
    }

    public void SendMessageToClient(string msg, NetworkConnection networkConnection)
    {
        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);


        //Driver.BeginSend(m_Connection, out var writer);
        DataStreamWriter streamWriter;
        //networkConnection.
        networkDriver.BeginSend(reliableAndInOrderPipeline, networkConnection, out streamWriter);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }

    public void SendLoginResponseToClient(string msg, bool loginResponse, NetworkConnection networkConnection)
    {
        if (loginResponse)
        {
            msg = "YES," + msg;
        }
        else
        {
            msg = "NO," + msg;
        }

        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);


        //Driver.BeginSend(m_Connection, out var writer);
        DataStreamWriter streamWriter;
        //networkConnection.
        networkDriver.BeginSend(reliableAndInOrderPipeline, networkConnection, out streamWriter);
        streamWriter.WriteInt(DataSignifiers.ServerLoginResponse);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }

    public void ProcessUserSignin(string username, string password, NetworkConnection networkConnection)
    {
        bool isValidUsername = false;
        bool isValidPassword = false;

        for (int i = 0; i < usernames.Count; i++)
        {
            if (username == usernames[i])
            {
                isValidUsername = true;

                if (password == passwords[i])
                {
                    isValidPassword = true;
                }

                break;
            }
        }

        if (isValidUsername)
        {
            if (isValidPassword)
            {
                SendLoginResponseToClient("Sign in approved", true, networkConnection);
            }
            else
            {
                SendLoginResponseToClient("Wrong password for this username", false, networkConnection);
            }
        }
        else
        {
            SendLoginResponseToClient("Username does not exist please proceed to sign up", false, networkConnection);
        }
    }

    public void ProcessUserSignup(string username, string password, NetworkConnection networkConnection)
    {
        bool isValid = true;

        foreach (string s in usernames)
        {
            if (username == s)
            {
                isValid = false;
                break;
            }
        }

        if (isValid)
        {
            usernames.Add(username);
            passwords.Add(password);

            SendLoginResponseToClient("New Account Created", true, networkConnection);

            SaveLoginInfo();
        }
        else
        {
            SendLoginResponseToClient("Account already exists please proceed to sign in", false, networkConnection);
        }
    }

    public void SaveLoginInfo()
    {
        StreamWriter sw = new StreamWriter(FilePathForSavedLoginInfo);

        for (int i = 0; i < usernames.Count; i++)
        {
            string info = usernames[i] + ',' + passwords[i];

            sw.WriteLine(info);
        }

        sw.Close();
    }

    public void LoadLoginInfo()
    {
        StreamReader sr = new StreamReader(FilePathForSavedLoginInfo);

        while (!sr.EndOfStream)
        {
            string info = sr.ReadLine();
            string[] infoSeparated = info.Split(',');
            
            usernames.Add(infoSeparated[0]);
            passwords.Add(infoSeparated[1]);
        }

        sr.Close();
    }

}
