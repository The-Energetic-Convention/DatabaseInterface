using System;
using System.Linq;
using System.IO.Pipes;
using TECDatabaseInterface.Models;
using System.Text;
using Azure;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;

public class DatabaseInteface {

    private static int numThreads = 40;

    public static void Main(string[] args)
    {
        try
        {
            int i;
            Thread[]? servers = new Thread[numThreads];

            Console.WriteLine("\n*** TEC Database Interface ***\n");
            Console.WriteLine("Server started, waiting for client connect...\n");
            for (i = 0; i < numThreads; i++)
            {
                servers[i] = new Thread(ServerThread);
                servers[i]?.Start();
            }
            Thread.Sleep(250);
            while (i > 0)
            {
                for (int j = 0; j < numThreads; j++)
                {
                    if (servers[j] != null)
                    {
                        if (servers[j]!.Join(50))
                        {
                            Console.WriteLine($"Server thread[{servers[j]!.ManagedThreadId}] finished.");
                            servers[j] = new Thread(ServerThread);
                            servers[j]?.Start();
                        }
                    }
                }
            }
            Console.WriteLine("\nServer threads exhausted, exiting.");
        }
        catch (Exception e) { Console.WriteLine(e.Message); Console.ReadLine(); }
    }

    private static void ServerThread(object? data)
    {
        NamedPipeServerStream pipeServer =
            new NamedPipeServerStream("TECDatabasePipe", PipeDirection.InOut, numThreads, PipeTransmissionMode.Byte, PipeOptions.WriteThrough);

        int threadId = Thread.CurrentThread.ManagedThreadId;

        // Wait for a client to connect
        pipeServer.WaitForConnection();

        Console.WriteLine($"Client connected on thread[{threadId}].");
        try
        {
            // Read the request from the client. Once the client has
            // written to the pipe its security token will be available.

            StreamString ss = new StreamString(pipeServer);
            string authkey = Environment.GetEnvironmentVariable("TECKEY") ?? "no key found";

            // Verify our identity to the connected client using a
            // string that the client anticipates.
            if (ss.ReadString() != authkey) { ss.WriteString("Unauthorized client!"); throw new Exception("Unauthorized client connection attemted!"); }
            ss.WriteString(authkey);
            string operation = ss.ReadString();

            // Determine what operation they want to do
            switch (operation)
            {
                case "C":
                    Create(ss);
                    break;
                case "R":
                    Read(ss);
                    break;
                case "U":
                    Update(ss);
                    break;
                case "D":
                    Delete(ss);
                    break;
                default:
                    OperationFail(ss, "Invaild operation");
                    break;
            }
            
        }
        // Catch any exception thrown just in case sumn happens
        catch (Exception e)
        {
            Console.WriteLine($"ERROR: {e.Message}");
        }
        pipeServer.Close();
    }

    static void Create(StreamString ss)
    {
        //output a ready message when ready to recieve the operation
        ss.WriteString("READY");

        // Do the operation
        string databsetype = ss.ReadString();
        ss.WriteString("READY");
        string operation = ss.ReadString();

        switch (databsetype)
        {
            case "U":
                UsersContext UserDB = new UsersContext(new DbContextOptionsBuilder<UsersContext>().EnableSensitiveDataLogging().Options);
                UserInfo? userInfo = JsonConvert.DeserializeObject<UserInfo>(operation);
                if (userInfo == null) { OperationFail(ss, "null user info"); return; }
                UserDB.Add(userInfo);
                UserDB.SaveChanges();
                UserDB.ChangeTracker.Clear();
                break;
            case "E":
                EventsContext EventDB = new EventsContext(new DbContextOptionsBuilder<EventsContext>().EnableSensitiveDataLogging().Options);
                EventInfo? eventInfo = JsonConvert.DeserializeObject<EventInfo>(operation);
                if (eventInfo == null) { OperationFail(ss, "null event info"); return; }
                EventDB.Add(eventInfo);
                EventDB.SaveChanges();
                EventDB.ChangeTracker.Clear();
                break;
            default:
                OperationFail(ss, "Invalid databse type");
                break;
        }

        OperationSuccess(ss, "Successful Create");
    }
    static void Read(StreamString ss)
    {
        //output a ready message when ready to recieve the operation
        ss.WriteString("READY");

        // Do the operation
        string databsetype = ss.ReadString();
        ss.WriteString("READY");
        string operation = ss.ReadString();

        switch (databsetype)
        {
            case "U":
                UsersContext UserDB = new UsersContext(new DbContextOptionsBuilder<UsersContext>().EnableSensitiveDataLogging().Options);
                if (operation.Contains("EMAIL="))
                {
                    //search by email
                    string email = operation.Replace("EMAIL=", "");
                    UserInfo[] users = UserDB.GetUsersByEmail(email).ToArray();
                    UserInfo? user = users.Length>0? users[0] : null;
                    ss.WriteString(JsonConvert.SerializeObject(users));
                }
                else if (operation.Contains("USERNAME="))
                {
                    //search by username
                    string username = operation.Replace("USERNAME=", "");
                    UserInfo[] users = UserDB.GetUsersByUsername(username).ToArray();
                    UserInfo? user = users.Length > 0 ? users[0] : null;
                    ss.WriteString(JsonConvert.SerializeObject(user));
                }
                else if (operation == "ALL")
                {
                    //return all
                    UserInfo[] users = UserDB.UserInfos.ToArray();
                    string serializedusers = JsonConvert.SerializeObject(users);
                    ss.WriteString(serializedusers);
                }
                else
                {
                    //get by id
                    int id = int.Parse(operation);
                    ss.WriteString(JsonConvert.SerializeObject(UserDB.Find<UserInfo>(id)));
                }
                UserDB.ChangeTracker.Clear();
                break;
            case "E":
                EventsContext EventDB = new EventsContext(new DbContextOptionsBuilder<EventsContext>().EnableSensitiveDataLogging().Options);
                switch (operation)
                {
                    case "ALL":
                        EventInfo[] events = [.. EventDB.EventInfos];
                        ss.WriteString(JsonConvert.SerializeObject(events));
                        break;
                    default:
                        int id = int.Parse(operation);
                        ss.WriteString(JsonConvert.SerializeObject(EventDB.Find<EventInfo>(id)));
                        break;
                }
                EventDB.ChangeTracker.Clear();
                break;
            default:
                OperationFail(ss, "Invalid databse type");
                break;
        }

        OperationSuccess(ss, "Successful Read");
    }
    static void Update(StreamString ss)
    {
        //output a ready message when ready to recieve the operation
        ss.WriteString("READY");

        // Do the operation
        string databsetype = ss.ReadString();
        ss.WriteString("READY");
        string operation = ss.ReadString();

        switch (databsetype)
        {
            case "U":
                UsersContext UserDB = new UsersContext(new DbContextOptionsBuilder<UsersContext>().EnableSensitiveDataLogging().Options);
                UserInfo? userInfo = JsonConvert.DeserializeObject<UserInfo>(operation);
                if (userInfo == null) { OperationFail(ss, "null user info"); return; }
                UserDB.Update(userInfo);
                UserDB.SaveChanges();
                UserDB.ChangeTracker.Clear();
                break;
            case "E":
                EventsContext EventDB = new EventsContext(new DbContextOptionsBuilder<EventsContext>().EnableSensitiveDataLogging().Options);
                EventInfo? eventInfo = JsonConvert.DeserializeObject<EventInfo>(operation);
                if (eventInfo == null) { OperationFail(ss, "null event info"); return; }
                EventDB.Update(eventInfo);
                EventDB.SaveChanges();
                EventDB.ChangeTracker.Clear();
                break;
            default:
                OperationFail(ss, "Invalid databse type");
                break;
        }

        OperationSuccess(ss, "Successful Update");
    }
    static void Delete(StreamString ss)
    {
        //output a ready message when ready to recieve the operation
        ss.WriteString("READY");

        // Do the operation
        string databsetype = ss.ReadString();
        ss.WriteString("READY");
        string operation = ss.ReadString();

        switch (databsetype)
        {
            case "U":
                UsersContext UserDB = new UsersContext(new DbContextOptionsBuilder<UsersContext>().EnableSensitiveDataLogging().Options);
                Console.WriteLine(operation);
                UserInfo? userInfo = JsonConvert.DeserializeObject<UserInfo>(operation);
                if (userInfo == null) { OperationFail(ss, "null user info"); return; }
                UserDB.Remove(userInfo);
                UserDB.SaveChanges();
                UserDB.ChangeTracker.Clear();
                break;
            case "E":
                EventsContext EventDB = new EventsContext(new DbContextOptionsBuilder<EventsContext>().EnableSensitiveDataLogging().Options);
                EventInfo? eventInfo = JsonConvert.DeserializeObject<EventInfo>(operation);
                if (eventInfo == null) { OperationFail(ss, "null event info"); return; }
                EventDB.Remove(eventInfo);
                EventDB.SaveChanges();
                EventDB.ChangeTracker.Clear();
                break;
            default:
                OperationFail(ss, "Invalid databse type");
                break;
        }

        OperationSuccess(ss, "Successful Delete");
    }

    static void OperationSuccess(StreamString ss, string message)
    {
        ss.WriteString("SUCCESS");
        Console.WriteLine(message);
    }

    static void OperationFail(StreamString ss, string message) 
    {
        ss.WriteString("FAIL");
        throw new Exception(message);
    }
}

public class StreamString
{
    private Stream ioStream;
    private UnicodeEncoding streamEncoding;

    public StreamString(Stream ioStream)
    {
        this.ioStream = ioStream;
        streamEncoding = new UnicodeEncoding();
    }

    public string ReadString()
    {
        int len = 0;

        len = ioStream.ReadByte() * 256;
        len += ioStream.ReadByte();
        byte[] inBuffer = new byte[len];
        ioStream.Read(inBuffer, 0, len);

        return streamEncoding.GetString(inBuffer);
    }

    public int WriteString(string outString)
    {
        byte[] outBuffer = streamEncoding.GetBytes(outString);
        int len = outBuffer.Length;
        if (len > UInt16.MaxValue)
        {
            len = (int)UInt16.MaxValue;
        }
        ioStream.WriteByte((byte)(len / 256));
        ioStream.WriteByte((byte)(len & 255));
        ioStream.Write(outBuffer, 0, len);
        ioStream.Flush();

        return outBuffer.Length + 2;
    }
}
