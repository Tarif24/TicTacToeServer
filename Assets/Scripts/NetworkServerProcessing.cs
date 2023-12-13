using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;


static public class NetworkServerProcessing
{
    #region SetUp

    static NetworkServer networkServer;

    static public void SetNetworkServer(NetworkServer NetworkServer)
    {
        networkServer = NetworkServer;
    }
    static public NetworkServer GetNetworkServer()
    {
        return networkServer;
    }

    #endregion

    #region Save and Load Data Functions

    static List<string> usernames = new List<string>();
    static List<string> passwords = new List<string>();

    static public string FilePathForSavedLoginInfo = "SaveLoginInformation.txt";

    static public void SaveLoginInfo()
    {
        StreamWriter sw = new StreamWriter(FilePathForSavedLoginInfo);

        for (int i = 0; i < usernames.Count; i++)
        {
            string info = usernames[i] + ',' + passwords[i];

            sw.WriteLine(info);
        }

        sw.Close();
    }

    static public void LoadLoginInfo()
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

    #endregion

    #region Send Data Functions

    static public void SendMessageToClient(string msg, NetworkConnection networkConnection)
    {
        networkServer.SendMessageToClient(msg, networkConnection);
    }

    static public void SendLoginResponseToClient(string msg, bool loginResponse, NetworkConnection networkConnection)
    {
        networkServer.SendLoginResponseToClient(msg, loginResponse, networkConnection);
    }

    static public void SendGameIDResponse(bool isBothPlayersIn, NetworkConnection connection, int gameRoomIndex)
    {
        networkServer.SendGameIDResponse(isBothPlayersIn, connection, gameRoomIndex);
    }

    static public void SendServerGameRoomKick(int gameRoomIndex)
    {
        networkServer.SendServerGameRoomKick(gameRoomIndex);
    }

    static public void SendServerSendToLookingForPlayer(NetworkConnection connection)
    {
        networkServer.SendServerSendToLookingForPlayer(connection);
    }

    static public void SendMessageFromOpponent(NetworkConnection connection, string msg)
    {
        networkServer.SendMessageFromOpponent(connection, msg);
    }

    static public void SendSelectionFromOpponent(NetworkConnection connectionToIgnore, int x, int y, int outcome, string marker, int gameRoomIndex)
    {
        networkServer.SendSelectionFromOpponent(connectionToIgnore, x, y, outcome, marker, gameRoomIndex);
    }

    static public void SendRequestSelectionsForObserver(NetworkConnection connection)
    {
        networkServer.SendRequestSelectionsForObserver(connection);
    }

    static public void SendSelectionsToObserver(NetworkConnection connection, List<int[]> allSelections)
    {
        networkServer.SendSelectionsToObserver(connection, allSelections);
    }

    #endregion

    #region Recive Data Functions

    static public List<string> allGameID = new List<string>();

    static public void ProcessDataTypeFromClient(DataStreamReader streamReader, NetworkConnection connection)
    {
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
                ProcessUserSignup(Info[0], Info[1], connection);
                break;

            case DataSignifiers.AccountSignin:
                ProcessUserSignin(Info[0], Info[1], connection);
                break;

            case DataSignifiers.Message:
                ProcessReceivedMsg(msg);
                break;

            case DataSignifiers.GameID:
                ProcessUserGameID(msg, connection);
                break;

            case DataSignifiers.BackOut:
                ProcessUserBackOut(msg, connection);
                break;

            case DataSignifiers.MessageToOpponent:
                ProcessMessageToOpponent(Info[0], Info[1], connection);
                break;

            case DataSignifiers.SelectionToOpponent:
                int x = streamReader.ReadInt();
                int y = streamReader.ReadInt();
                int outcome = streamReader.ReadInt();
                ProcessSelectionToOpponent(msg, x, y, outcome, connection);
                break;

            case DataSignifiers.AllSelectionsToObserver:

                List<int[]> allSelections = new List<int[]>();

                while (streamReader.ReadInt() == 1)
                {
                    int[] temp = new int[3];

                    temp[0] = streamReader.ReadInt();
                    temp[1] = streamReader.ReadInt();
                    temp[2] = streamReader.ReadInt();

                    allSelections.Add(temp);
                }

                ProcessSelectionsToObserver(msg, allSelections);
                break;

        }

        buffer.Dispose();
    }

    static public void ProcessReceivedMsg(string msg)
    {
        Debug.Log("Msg received = " + msg);
    }

    static public void ProcessUserSignin(string username, string password, NetworkConnection networkConnection)
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

    static public void ProcessUserSignup(string username, string password, NetworkConnection networkConnection)
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

    static public void ProcessUserGameID(string gameID, NetworkConnection connection)
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

        if (isInList && !networkServer.connectionsForEachRoom[index][1].IsCreated)
        {

            networkServer.connectionsForEachRoom[index][1] = connection;

            SendGameIDResponse(true, connection, index);

        }
        else if (isInList)
        {
            networkServer.connectionsForEachRoom[index].Add(connection);
            SendGameIDResponse(true, connection, index);
            SendRequestSelectionsForObserver(networkServer.connectionsForEachRoom[index][0]);
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

            networkServer.connectionsForEachRoom.Add(tempConnectionsForGameRoom);

            SendGameIDResponse(false, connection, allGameID.Count - 1);
        }
    }

    static public void ProcessUserBackOut(string gameID, NetworkConnection connection)
    {
        int index;

        for (index = 0; index < allGameID.Count; index++)
        {
            if (gameID == allGameID[index])
            {
                break;
            }
        }

        if (networkServer.connectionsForEachRoom[index][0] == connection)
        {
            if (!networkServer.connectionsForEachRoom[index][1].IsCreated)
            {
                allGameID.Remove(gameID);
                SendServerGameRoomKick(index);
                networkServer.connectionsForEachRoom.RemoveAt(index);
            }
            else
            {
                networkServer.connectionsForEachRoom[index][0] = networkServer.connectionsForEachRoom[index][1];
                networkServer.connectionsForEachRoom[index][1] = new NetworkConnection();
                SendServerSendToLookingForPlayer(networkServer.connectionsForEachRoom[index][0]);
                SendServerGameRoomKick(index);
            }

        }
        else if (networkServer.connectionsForEachRoom[index][1] == connection)
        {
            networkServer.connectionsForEachRoom[index][1] = new NetworkConnection();
            SendServerSendToLookingForPlayer(networkServer.connectionsForEachRoom[index][0]);
        }
        else
        {
            networkServer.connectionsForEachRoom[index].Remove(connection);
        }
    }

    static public void ProcessMessageToOpponent(string gameID, string msg, NetworkConnection connection)
    {
        int index;

        for (index = 0; index < allGameID.Count; index++)
        {
            if (gameID == allGameID[index])
            {
                break;
            }
        }

        if (networkServer.connectionsForEachRoom[index][0] == connection)
        {
            SendMessageFromOpponent(networkServer.connectionsForEachRoom[index][1], msg);
        }
        else
        {
            SendMessageFromOpponent(networkServer.connectionsForEachRoom[index][0], msg);
        }
    }

    static public void ProcessSelectionToOpponent(string gameID, int x, int y, int outcome, NetworkConnection connection)
    {
        int index;

        for (index = 0; index < allGameID.Count; index++)
        {
            if (gameID == allGameID[index])
            {
                break;
            }
        }

        if (networkServer.connectionsForEachRoom[index][0] == connection)
        {
            SendSelectionFromOpponent(networkServer.connectionsForEachRoom[index][0], x, y, outcome, "X", index);
        }
        else if (networkServer.connectionsForEachRoom[index][1] == connection)
        {
            SendSelectionFromOpponent(networkServer.connectionsForEachRoom[index][1], x, y, outcome, "O", index);
        }
    }

    static public void ProcessSelectionsToObserver(string gameID, List<int[]> allSelections)
    {
        int index;

        for (index = 0; index < allGameID.Count; index++)
        {
            if (gameID == allGameID[index])
            {
                break;
            }
        }

        for (int i = 2; i < networkServer.connectionsForEachRoom[index].Count; i++)
        {
            SendSelectionsToObserver(networkServer.connectionsForEachRoom[index][i], allSelections);
        }
    }

    #endregion
}
