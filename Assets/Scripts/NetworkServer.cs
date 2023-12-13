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
using System;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 54321;

    const int MaxNumberOfClientConnections = 1000;

    [SerializeField]
    public List<List<NetworkConnection>> connectionsForEachRoom;

    void Start()
    {
        NetworkServerProcessing.SetNetworkServer(this);

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

        connectionsForEachRoom = new List<List<NetworkConnection>>();

        if (File.Exists(NetworkServerProcessing.FilePathForSavedLoginInfo))
        {
            NetworkServerProcessing.LoadLoginInfo();
        }
    }

    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {

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
                switch (networkEventType)
                {
                    case NetworkEvent.Type.Data:

                        NetworkServerProcessing.ProcessDataTypeFromClient(streamReader, networkConnections[i]);

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

    #region Send Data Functions

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

    public void SendGameIDResponse(bool isBothPlayersIn, NetworkConnection connection, int gameRoomIndex)
    {
       
        foreach (NetworkConnection c in connectionsForEachRoom[gameRoomIndex])
        {
            if (c.IsCreated)
            {
                string temp = " ";
                int state = 0;
                bool sendData = false;

                if (isBothPlayersIn)
                {
                    if (connectionsForEachRoom[gameRoomIndex][0] == connection || connectionsForEachRoom[gameRoomIndex][1] == connection)
                    {
                        if (connectionsForEachRoom[gameRoomIndex][0] == c)
                        {
                            sendData = true;
                            state = (int)GameStates.PlayerMove;
                            temp = "X";
                        }
                        else if (connectionsForEachRoom[gameRoomIndex][1] == c)
                        {
                            sendData = true;
                            state = (int)GameStates.OpponentMove;
                            temp = "O";
                        }
                    }
                    else if (connectionsForEachRoom[gameRoomIndex][0] != c && connectionsForEachRoom[gameRoomIndex][1] != c)
                    {
                        sendData = true;
                        state = (int)GameStates.Observer;
                        temp = "T";
                    }
                }
                else
                {
                    sendData = true;
                    state = (int)GameStates.LookingForPlayer;
                }

                if (sendData)
                {
                    DataStreamWriter streamWriter;
                    //networkConnection.
                    networkDriver.BeginSend(reliableAndInOrderPipeline, c, out streamWriter);
                    streamWriter.WriteInt(DataSignifiers.ServerGameIDResponse);
                    byte[] msgAsByteArray = Encoding.Unicode.GetBytes(temp);
                    NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);
                    streamWriter.WriteInt(state);
                    streamWriter.WriteInt(buffer.Length);
                    streamWriter.WriteBytes(buffer);

                    networkDriver.EndSend(streamWriter);

                    buffer.Dispose();
                }
            }
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

    public void SendMessageFromOpponent(NetworkConnection connection, string msg)
    {
        string temp = "Message: " + msg;

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

    public void SendSelectionFromOpponent(NetworkConnection connectionToIgnore, int x, int y, int outcome, string marker, int gameRoomIndex)
    {
        foreach (NetworkConnection c in connectionsForEachRoom[gameRoomIndex])
        {
            if (c != connectionToIgnore)
            {
                byte[] msgAsByteArray = Encoding.Unicode.GetBytes(marker);
                NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);


                DataStreamWriter streamWriter;
                //networkConnection.
                networkDriver.BeginSend(reliableAndInOrderPipeline, c, out streamWriter);
                streamWriter.WriteInt(DataSignifiers.SelectionToOpponent);
                streamWriter.WriteInt(x);
                streamWriter.WriteInt(y);
                streamWriter.WriteInt(outcome);
                streamWriter.WriteInt(buffer.Length);
                streamWriter.WriteBytes(buffer);
                networkDriver.EndSend(streamWriter);

                buffer.Dispose();
            }
        }
    }

    public void SendRequestSelectionsForObserver(NetworkConnection connection)
    {
        DataStreamWriter streamWriter;

        networkDriver.BeginSend(reliableAndInOrderPipeline, connection, out streamWriter);
        streamWriter.WriteInt(DataSignifiers.AllSelectionsToObserver);

        networkDriver.EndSend(streamWriter);
    }

    public void SendSelectionsToObserver(NetworkConnection connection, List<int[]> allSelections)
    {
        DataStreamWriter streamWriter;
        networkDriver.BeginSend(reliableAndInOrderPipeline, connection, out streamWriter);
        streamWriter.WriteInt(DataSignifiers.AllSelectionsToObserverFinal);

        for (int i = 0; i < allSelections.Count; i++)
        {
            streamWriter.WriteInt(1);
            streamWriter.WriteInt(allSelections[i][0]);
            streamWriter.WriteInt(allSelections[i][1]);
            streamWriter.WriteInt(allSelections[i][2]);
        }

        streamWriter.WriteInt(0);

        networkDriver.EndSend(streamWriter);
    }

    #endregion

}
