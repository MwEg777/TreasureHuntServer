using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Net;
using System.Linq;
using Newtonsoft.Json;
using CoreExtensions;
using System.Threading.Tasks;
using System.Threading;

namespace MythServer
{
    class Methods
    {

        public List<Player> players = new List<Player>();
        public List<Room> rooms = new List<Room>();

        public static Methods instance;

        public static int secondsSinceStartUp = 0,
            playerTimeoutMax = 5;

        public Methods()
        {

            instance = this;
            Thread playersTimeoutCheckerThread = new Thread(PlayersTimeoutChecker);
            playersTimeoutCheckerThread.Start();
            CreateNewRoom();

        }

        async void PlayersTimeoutChecker() 
        { 

	        while(true)
            {

                await Task.Delay(1000);

                secondsSinceStartUp++;

                try
                {

                    List<Player> playersToRemove = new List<Player>();

                    foreach(Player player in players)
                    {

                        //Console.WriteLine("Looping over player " + player.name + ". Time now is: " + secondsSinceStartUp + " and player time is: " + player.secondsSinceLastValidMessage);

                        if (secondsSinceStartUp - player.secondsSinceLastValidMessage > playerTimeoutMax || secondsSinceStartUp - player.secondsSinceLastValidMessage < - playerTimeoutMax)
                        {

                            try
                            {

                                try
                                {
                                    player.connection.Close();
                                    Console.WriteLine("Player " + player.name + " timed out. Disconnecting..");
                                    player.online = false;
                                    if (player.room != null)
                                    {
                                        Console.WriteLine("The disconnected player was in room " + player.room.id + ", removing him from it first.");
                                        player.room.RemovePlayerFromRoom(player);
                                    }
                                    playersToRemove.Add(player);
                                    Console.WriteLine("Player " + player.name + " disconnected.");
                                }
                                catch(Exception eClose)
                                {
                                    Console.WriteLine("Couldn't close connection with player " + player.name + ". " + eClose);
                                }

                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine("Couldn't disconnect player and remove him. " + ex);
                            }

                        }

                    }

                    foreach (Player player in playersToRemove)
                        players.Remove(player);

                }
                catch(Exception e)
                {
                    Console.WriteLine("No IDEAR why an error happened here. " + e);
                }

            }

        }

        public Player AddPlayer(TcpClient conn)
        {

            Player player = new Player
            { connection = conn };
            player.udpIPEndPoint = (IPEndPoint)conn.Client.RemoteEndPoint;
            players.Add(player);
            return player;

        }

        public Player GetPlayerByID(string ID)
        {

            Console.WriteLine("Players in list are: ");

            foreach(Player player in players)
                Console.WriteLine("Player id: " + player.name);

            return players.Find(p => p.id == ID);

        }

        public Room CreateNewRoom()
        {

            Room room = new Room();
            room.id = Guid.NewGuid().ToString();
            room.roomState = Room.State.Matchmaking;
            rooms.Add(room);
            return room;

        }

        public void PeriodicTreasuresCheck()
        {

        }

        #region PlayerRequests

        public void WHOAMI(Player player, Dictionary<string, object> payload)
        {

            player.id = payload["id"].ToString();
            player.name = payload["name"].ToString();
            player.online = true;

        }

        public void COLLECT_TREASURE(Player player, Dictionary<string, object> payload)
        {

            player.room.CollectTreasure(player, payload["treasureID"].ToString());

        }

        public void START_MATCHMAKING(Player player, Dictionary<string, object> payload)
        {

            Room firstSuitableRoom = null;

            foreach (Room room in rooms)
                if (!room.isFull() && room.roomState == Room.State.Matchmaking)
                {
                    firstSuitableRoom = room;
                    break;
                }

            if (firstSuitableRoom == null)
                firstSuitableRoom = CreateNewRoom();

            firstSuitableRoom.AddPlayerToRoom(player);

        }

        public void CANCEL_MATCHMAKING(Player player, Dictionary<string, object> payload)
        {

            if (player.room == null)
                return;

            player.room.RemovePlayerFromRoom(player);

        }

        public void IAM_ALIVE(Player player, Dictionary<string, object> payload)
        {

            //Console.WriteLine("Player sent a pulse! Player ID is: " + player.name);

        }

        public void UDP_PULSE(Player player, Dictionary<string, object> payload)
        {

            Console.WriteLine("Player " + player.name + " updated his UDP port to: " + player.udpIPEndPoint.Port);

            Server.SendMessageUDP(player, R_HEY());

        }

        #endregion

        #region ServerResponses

        public static string R_HEY()
        {

            Dictionary<string, object> toConvert = new Dictionary<string, object>();

            toConvert.Add("type", "heylol");
            
            return toConvert.ToJson();

        }

        public static string R_LOGMESSAGE(string message)
        {

            Dictionary<string, object> toConvert = new Dictionary<string, object>();

            toConvert.Add("type", "logmessage");
            toConvert.Add("message", message);

            return toConvert.ToJson();

        }

        public static string R_SENDTREASURES(Room room)
        {

            Dictionary<string, object> toConvert = new Dictionary<string, object>();

            toConvert.Add("type", "PLACE_TREASURES");
            List<TreasureInfo> treasureInfos = new List<TreasureInfo>();

            foreach (Treasure treasure in room.activeTreasures)
                treasureInfos.Add(treasure.treasureInfo);
            string jsonToSend = JsonConvert.SerializeObject(treasureInfos);

            toConvert.Add("treasures", jsonToSend);

            return toConvert.ToJson();

        }

        #endregion

    }

    class Player
    {

        #region Essentials

        public string id = "no_id", name = "no_name";
        public bool loaded = false, online = false;
        public TcpClient connection;
        public IPEndPoint udpIPEndPoint;
        public Room room;
        public long secondsSinceLastValidMessage = Methods.secondsSinceStartUp;

        #endregion

        #region GameSpecificStuff



        #endregion

    }

    class Room
    {

        #region Essentials

        public string id;
        public enum State { Matchmaking, Ongoing, Ended };
        public State roomState = State.Matchmaking;
        public List<Player> players = new List<Player>();
        public int maxPlayers = 1;

        #endregion

        #region GameSpecificStuff

        public List<Treasure> activeTreasures = new List<Treasure>();

        public void AddRandomTreasure()
        {

            Treasure treasure = new Treasure
            {

                treasureInfo = new TreasureInfo {
                    posX = new Random().Next(1, 5),
                    posY = new Random().Next(1, 5)
                },
                room = this

            };

            activeTreasures.Add(treasure);

        }

        public void StartMinigame(Treasure treasureToBattleOver)
        {

            foreach(Player player in treasureToBattleOver.collectors.Keys)
            {

                if (player != null)
                    Server.SendMessageTCP(player, Methods.R_LOGMESSAGE("Started minigame!"));

            }

        }

        public void CollectTreasure(Player collector, string treasureID)
        {

            Treasure treasureToCollect = activeTreasures.Find(t => t.treasureInfo.treasureID == treasureID);

            if (treasureToCollect == null) //Treasure was already collected
            {

                Server.SendMessageTCP(collector, Methods.R_LOGMESSAGE("You are trying to collect a treasure that's already collected."));

            }
            else
            {

                //Check if the treasure can be battled over, so it can have a minigame
                if (treasureToCollect.canBeBattledOver)
                {
                    
                    if (!treasureToCollect.battleStarted) //Battle didn't start yet.
                    {

                        if (!treasureToCollect.collectors.ContainsKey(collector))
                        { 

                            treasureToCollect.collectors.Add(collector, DateTime.UtcNow);

                            //if battle window is not opened, open it and wait for window time
                            if (treasureToCollect.BattleTimeoutThread == null)
                            { 
                                treasureToCollect.BattleTimeoutThread = new Thread(treasureToCollect.BattleTimeoutChecker);
                                treasureToCollect.BattleTimeoutThread.Start();
                            }
                            else //battle window is already opened. You're the second player. Close the window and start the minigame.
                            {

                                treasureToCollect.BattleTimeoutThread.Interrupt();
                                StartMinigame(treasureToCollect);

                            }

                        }
                        else
                        {

                            Console.WriteLine("Player is trying to collect a treasure twice: " + collector.id);

                        }

                    }
                    else
                    {

                        //Battle already started. Do nothing.

                    }

                }
                else //Can be collected by only one player, no battles
                {

                    GiveTreasure(collector, treasureID);

                }

            }

        }

        public void GiveTreasure(Player collector, string treasureID)
        {

            Treasure treasureToCollect = activeTreasures.Find(t => t.treasureInfo.treasureID == treasureID);

            if (treasureToCollect == null) //Treasure was already collected
            {

                Server.SendMessageTCP(collector, Methods.R_LOGMESSAGE("You are trying to get a treasure that's already retrieved by another player."));

            }

            if (activeTreasures.Remove(treasureToCollect))
            {

                Console.WriteLine("A treasure was just given to player with playerID: " + collector.id + "\nTreasure ID: " + treasureID);
                Server.SendMessageTCP(collector, Methods.R_LOGMESSAGE("You just collected treasure " + treasureID));
                BroadcastInRoom(Methods.R_SENDTREASURES(this));

            }
            else
            {

                Console.WriteLine("Treasure couldn't be given to player " + collector.id);
                Server.SendMessageTCP(collector, Methods.R_LOGMESSAGE("Couldn't give treasure to you."));

            }

        }

        #endregion

        public Room()
        {

            for (int i=0; i<4; i++)
                AddRandomTreasure();

        }

        public bool isFull()
        {

            //return players.Count == maxPlayers;
            return false; //TODO: Uncomment the previous line if you want room players to be limited to maxPlayers

        }

        public bool AddPlayerToRoom(Player player)
        {
            
            if (isFull())
            {

                Console.WriteLine("Player " + player.name + " tried to join room " + id + " but it's full.");
                return false;

            }
            else
            {

                foreach(Room room in Methods.instance.rooms) //Remove player from any other room he's in
                {

                    foreach(Player p in room.players)
                    {
                        if (p.id == player.id)
                        {

                            Console.WriteLine("Player " + p.name + " was found in another room! Removing him from it..");
                            RemovePlayerFromRoom(player);

                        }
                    }

                }

                players.Add(player);
                Console.WriteLine("Player " + player.name + " joined room " + id);
                Server.SendMessageTCP(player, Methods.R_LOGMESSAGE("You just joined room " + id));
                player.room = this;
                player.loaded = false;
                Server.SendMessageTCP(player, Methods.R_SENDTREASURES(this));

                //Check if room is ready to start the match
                if (isFull())
                {

                    Console.WriteLine("Room " + id + "\nis now ready to start the match!");

                }

                PrintRoomInfo();

                return true;

            }


        }

        public void RemovePlayerFromRoom(Player player)
        {

            Console.WriteLine("Trying to remove player " + player.name + " from room " + id);
            if (players.Remove(players.Find(p => p.id == player.id)))
                Console.WriteLine("Player " + player.name + " left room " + id);
            else
                Console.WriteLine("Player " + player.name + " couldn't leave room " + id);

            //Check if room is now empty, then remove it from rooms list.
            //TODO: Auto comment this part for auto room removal
            //if (players.Count == 0)
            //{

            //    Console.WriteLine("Removing room because it's now empty.");
            //    Methods.instance.rooms.Remove(this);

            //}

            PrintRoomInfo();

        }

        public void PrintRoomInfo()
        {

            Console.WriteLine("Room ID: " + id);
            Console.WriteLine("Room players (" + players.Count + "):");
            foreach(Player player in players)
                Console.WriteLine("     Player " + player.name);

        }

        public void BroadcastInRoom(string messageToBroadcast)
        {

            foreach(Player player in players)
            {

                try
                {

                    Server.SendMessageTCP(player, messageToBroadcast);

                }
                catch(Exception ex)
                {

                    Console.WriteLine("Couldn't broadcast room message to one player: " + player.name + ".\nException: " + ex);

                }

            }

        }

        public void BroadcastInRoomToOpponentsOnly(string messageToBroadcast, Player sender)
        {

            foreach (Player player in players)
            {

                try
                {

                    if (player == sender)
                        continue;

                    Server.SendMessageTCP(player, messageToBroadcast);

                }
                catch (Exception ex)
                {

                    Console.WriteLine("Couldn't broadcast room message to one player: " + player.name + ".\nException: " + ex);

                }

            }

        }

    }

    class Treasure
    {

        public Room room;
        public TreasureInfo treasureInfo;
        public bool canBeBattledOver = true; //True if this treasure can have a minigame over
        public bool battleStarted = false;
        public int battleWindowMilliseconds = 500; //If two users collect it within 500 milliseconds, a minigame will start
        public Dictionary<Player, DateTime> collectors = new Dictionary<Player, DateTime>(); //Key is collector, value is the time he collected it at
        public Thread BattleTimeoutThread;

        public async void BattleTimeoutChecker(object obj)
        {

            await Task.Delay(battleWindowMilliseconds);

            try
            {

                if (collectors.Count < 2) // Only one player collected the treasure through the battle window time
                {

                    //Give the treasure to that player
                    if (collectors.Keys.First() != null)
                    {

                        room.GiveTreasure(collectors.Keys.First(), treasureInfo.treasureID);

                    }

                }
                else
                {

                    //Do nothing. Or up to you.

                }

            }
            catch (Exception e)
            {
                Console.WriteLine("An error occurred. " + e);
            }

        }

    }

    class TreasureInfo
    {

        public string treasureID = Guid.NewGuid().ToString();
        public float posX, posY;

    }

}
