using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using System.Xml.Serialization;
using UnityEditor.MemoryProfiler;

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

    [SerializeField]
    List<string> allGameID;

    [SerializeField]
    List<List<NetworkConnection>> connectionsForEachRoom;

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
        connectionsForEachRoom = new List<List<NetworkConnection>>();

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
                //if (pipelineUsedToSendEvent == reliableAndInOrderPipeline)
                //    Debug.Log("Network event from: reliableAndInOrderPipeline");
                //else if (pipelineUsedToSendEvent == nonReliableNotInOrderedPipeline)
                //    Debug.Log("Network event from: nonReliableNotInOrderedPipeline");

                switch (networkEventType)
                {
                    case NetworkEvent.Type.Data:
                        int dataSignifier = streamReader.ReadInt();

                        int sizeOfDataBuffer = streamReader.ReadInt();
                        NativeArray<byte> buffer = new NativeArray<byte>(sizeOfDataBuffer, Allocator.Persistent);
                        streamReader.ReadBytes(buffer);
                        byte[] byteBuffer = buffer.ToArray();
                        string msg = Encoding.Unicode.GetString(byteBuffer);
                        
                        string[] Info = msg.Split(',');

                        switch (dataSignifier)
                        {
                            case DataSignifiers.AccountSignup:
                                ProcessUserSignup(Info[0], Info[1], networkConnections[i]);
                                break;

                            case DataSignifiers.AccountSignin:
                                ProcessUserSignin(Info[0], Info[1], networkConnections[i]);
                                break;

                            case DataSignifiers.Message:
                                ProcessReceivedMsg(msg);
                                break;

                            case DataSignifiers.GameID:
                                ProcessUserGameID(msg, networkConnections[i]);
                                break;

                            case DataSignifiers.BackOut:
                                ProcessUserBackOut(msg, networkConnections[i]);
                                break;

                            case DataSignifiers.MessageToOpponent:
                                ProcessMessageToOpponent(Info[0], Info[1], networkConnections[i]);
                                break;

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

    public void ProcessUserGameID(string gameID, NetworkConnection connection)
    {
        bool isInList = false;

        int index;

        for (index = 0; index < allGameID.Count; index++)
        {
            if (gameID == allGameID[index])
            {
                isInList = true;
                break;
            }
        }

        if (isInList && !connectionsForEachRoom[index][1].IsCreated)
        {
            
            connectionsForEachRoom[index][1] = connection;

            SendGameIDResponse(true, connection, index);
            
        }
        else
        {
            allGameID.Add(gameID);

            List<NetworkConnection> tempConnectionsForGameRoom;
            tempConnectionsForGameRoom = new List<NetworkConnection>()
            {
                connection,
                new NetworkConnection()
            };

            connectionsForEachRoom.Add(tempConnectionsForGameRoom);

            SendGameIDResponse(false, connection, allGameID.Count - 1);
        }
    }

    public void SendGameIDResponse(bool isBothPlayersIn, NetworkConnection connection, int gameRoomIndex)
    {
       
        foreach (NetworkConnection c in connectionsForEachRoom[gameRoomIndex])
        {
            if (c.IsCreated)
            {
                DataStreamWriter streamWriter;
                //networkConnection.
                networkDriver.BeginSend(reliableAndInOrderPipeline, c, out streamWriter);
                streamWriter.WriteInt(DataSignifiers.ServerGameIDResponse);

                if (isBothPlayersIn)
                {
                    if (connectionsForEachRoom[gameRoomIndex][0] == connection || connectionsForEachRoom[gameRoomIndex][1] == connection)
                    {
                        if (connectionsForEachRoom[gameRoomIndex][0] == c)
                        {
                            streamWriter.WriteInt((int)GameStates.PlayerMove);
                        }
                        else if (connectionsForEachRoom[gameRoomIndex][1] == c)
                        {
                            streamWriter.WriteInt((int)GameStates.OpponentMove);
                        }
                    }
                }
                else
                {
                    streamWriter.WriteInt((int)GameStates.LookingForPlayer);
                }

                networkDriver.EndSend(streamWriter);
            }
        }
        
    }

    public void ProcessUserBackOut(string gameID, NetworkConnection connection)
    {
        int index;

        for (index = 0; index < allGameID.Count; index++)
        {
            if (gameID == allGameID[index])
            {
                break;
            }
        }

        if (connectionsForEachRoom[index][0] == connection)
        {
            if (!connectionsForEachRoom[index][1].IsCreated)
            {
                allGameID.Remove(gameID);
                SendServerGameRoomKick(index);
                connectionsForEachRoom.RemoveAt(index);
            }
            else
            {
                connectionsForEachRoom[index][0] = connectionsForEachRoom[index][1];
                connectionsForEachRoom[index][1] = new NetworkConnection();
                SendServerSendToLookingForPlayer(connectionsForEachRoom[index][0]);
                SendServerGameRoomKick(index);
            }
            
        }
        else if (connectionsForEachRoom[index][1] == connection)
        {
            connectionsForEachRoom[index][1] = new NetworkConnection();
            SendServerSendToLookingForPlayer(connectionsForEachRoom[index][0]);
        }
        else
        {
            connectionsForEachRoom[index].Remove(connection);
        }
    }

    public void SendServerGameRoomKick(int gameRoomIndex)
    {
        for (int i = 2; i < connectionsForEachRoom[gameRoomIndex].Count; i++)
        {
            DataStreamWriter streamWriter;

            NetworkConnection c = connectionsForEachRoom[gameRoomIndex][i];

            networkDriver.BeginSend(reliableAndInOrderPipeline, c, out streamWriter);
            streamWriter.WriteInt(DataSignifiers.ServerGameRoomKick);

            networkDriver.EndSend(streamWriter);
        }
    }

    public void SendServerSendToLookingForPlayer(NetworkConnection connection)
    {
        DataStreamWriter streamWriter;

        networkDriver.BeginSend(reliableAndInOrderPipeline, connection, out streamWriter);
        streamWriter.WriteInt(DataSignifiers.ServerSendToLookingForPlayer);

        networkDriver.EndSend(streamWriter);
    }

    public void ProcessMessageToOpponent(string gameID, string msg, NetworkConnection connection)
    {
        int index;

        for (index = 0; index < allGameID.Count; index++)
        {
            if (gameID == allGameID[index])
            {
                break;
            }
        }

        if (connectionsForEachRoom[index][0] == connection)
        {
            SendMessageFromOpponent(connectionsForEachRoom[index][1], msg);
        }
        else
        {
            SendMessageFromOpponent(connectionsForEachRoom[index][0], msg);
        }
    }

    public void SendMessageFromOpponent(NetworkConnection connection, string msg)
    {
        string temp = "Message From Opponent: " + msg;

        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(temp);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);

        DataStreamWriter streamWriter;
        //networkConnection.
        networkDriver.BeginSend(reliableAndInOrderPipeline, connection, out streamWriter);
        streamWriter.WriteInt(DataSignifiers.MessageToOpponent);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }

}
