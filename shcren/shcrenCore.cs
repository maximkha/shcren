using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Xml.Serialization;
using System.Threading;
using System.Timers;

namespace shcren
{
    public static class shcrenCore
    {
        public static readonly string version = "1.0";

        [Serializable]
        public class task
        {
            public int lastPID;
            public string comm;
            public string status;
        }

        [Serializable]
        public class server
        {
            public string sshAddress;
            public int port = 22;
            public List<task> taskList;
            private session parentSession;

            public string getStatus()
            {
                //return "Online";
                if (!online()) return "Offline";
                if (taskList.Count == 0) return "Idle";
                if (taskList.All(x=>x.status=="Stopped")) return "Idle";
                if (taskList.Count > 0) return "Running";
                return "Internal Error";
            }

            public bool online()
            {
                return utilities.isOnline(sshAddress);
            }

            public void updateTasks()
            {
                List<int> runningPids = getRunningScreenPIDS();

                for (int i = 0; i < taskList.Count; i++)
                    if(!runningPids.Contains(taskList[i].lastPID)) taskList[i].status = "Stopped";
            }

            public string query(string command)
            {
                return utilities.sshExec(sshAddress, command, parentSession, port);
            }

            public bool copyFileToRemote(string filename, string destinationDirectory)
            {
                string cdCommand = "";
                if (destinationDirectory != "") cdCommand = "cd " + destinationDirectory + " ;";
                return utilities.exec(parentSession.whichbash, "-c 'tar czf - \'" + filename +"\' | ssh -p "+ port + " " + sshAddress +" \"( " + cdCommand + " cat > tmp.tar ; tar -xvf tmp.tar ; rm tmp.tar )\"'") == filename;
            }

            public void init(session p)
            {
                parentSession = p;
            }

            public List<int> getRunningScreenPIDS()
            {
				List<int> runningPids = new List<int>();
				string res = query("screen -ls");
				List<string> pts = res.Split('\n').ToList();
				pts.RemoveAt(0); //Removes: "There are screens on:"
				pts.RemoveAt(pts.Count); //Removes "x Sockets"
				foreach (string part in pts)
					runningPids.Add(Convert.ToInt32(part.Split('.')[0]));
                return runningPids;
            }

            public void submitTask(string command)
            {
                List<int> runningPids = getRunningScreenPIDS();
                query("screen -dm " + command);
				List<int> newRunningPids = getRunningScreenPIDS();

                task t = new task();
                t.lastPID = runningPids.Where(x => !newRunningPids.Contains(x)).ToList()[0];
                t.status = "Running";
                t.comm = command;
                taskList.Add(t);
            }

            public string sessionNameFromPID(int pid)
            {
                string ret = query("screen -ls | grep " + pid + " |  awk '{print $1}'");
                if (ret == "")
                    throw new Exception("Non-Existant PID");
                if (ret.Contains('\n'))
                    throw new Exception("Multiple sessions with that PID");
                return ret;
            }

            public void terminateTask(int taskIndex)
            {
                task t = taskList[taskIndex];
                string sessionName = sessionNameFromPID(t.lastPID);
                query("screen -S " + sessionName + " -X quit");
                taskList.RemoveAt(taskIndex);
            }

            public double getCpuPercent()
            {
                return Convert.ToDouble(query("grep 'cpu ' /proc/stat | awk '{usage=($2+$4)*100/($2+$4+$5)} END {print usage}'"));
            }

            public double getMemPercent()
            {
                return Convert.ToDouble(query("free | grep Mem | awk '{print $3/$2 * 100.0}'"));
            }

            public void deleteTask(int i)
            {
                updateTasks();
                if (taskList[i].status == "Running")
                { terminateTask(i); return; }
                taskList.RemoveAt(i);
            }
        }

        [Serializable]
        public class session
        {
            public string whichssh = "/usr/bin/ssh";
            public string whichtar = "/usr/bin/tar";
            public string whichbash = "/bin/bash";

            public List<server> serverList = new List<server>();

            public void addServer(string sshAddress, int port)
            {
                if (!utilities.isOnline(sshAddress)) Console.WriteLine("Warning {0} is unreachable", utilities.ipFromSshAddr(sshAddress));
                server newserver = new server();
                newserver.port = port;
                newserver.sshAddress = sshAddress;
                serverList.Add(newserver);
            }

            public Dictionary<string, string> getAllStatuses() //All stati XD
            {
                Dictionary<string, string> hostnameStatus = new Dictionary<string, string>();
                foreach (server ts in serverList)
                {
                    if(ts.getStatus() == "Offline") 
                    {
                        hostnameStatus.Add(utilities.ipFromSshAddr(ts.sshAddress), "Offline");
                        continue;
                    }
                    ts.updateTasks();
                    hostnameStatus.Add(utilities.ipFromSshAddr(ts.sshAddress), ts.getStatus());
                }
                return hostnameStatus;
            }
        }

        public class cliSession
        {
            session selectedSession;
            System.Timers.Timer infoBarUpdater;
            int infoBarHeight = -1;
            bool serverSelected = false;
            int selectedServer = -1;

            public void start()
            {
				bool skipLoad = false;
				Console.WriteLine("Welcome to the shcrenCore CLI version {0}", version);
				if (!File.Exists("currentSession.xml"))
				{
					Console.WriteLine("No session found!");
					int option = ui.selectMenu1(new string[] { "Create", "Exit" }, new char[] { 'C', 'E' });

					if (option == 0)
					{
						Console.WriteLine("Creating!");
						selectedSession = new session();
						utilities.saveSessionXML("currentSession.xml", selectedSession);
						skipLoad = true;
					}

					if (option == 1)
					{
						Console.WriteLine("Bye!");
						Thread.Sleep(500);
						Environment.Exit(1);
					}
				}

				if (!skipLoad)
				{
					Console.WriteLine("Loading last session");
					selectedSession = utilities.loadSessionXML("currentSession.xml");
				}

                infoBarUpdater = new System.Timers.Timer(30 * 1000);
                infoBarUpdater.Elapsed += updateBridge;
                infoBarUpdater.Start();

                infoBarHeight = selectedSession.serverList.Count + 2;

                ui.terminal.clearConsole();
                drawInfoBar();
				terminal();
            }

            public void terminal()
            {
				Console.SetCursorPosition(0, infoBarHeight);
                while(true)
                {
                    Console.Write(">");
                    string comm = Console.ReadLine();
                    Console.WriteLine(command(comm));
                    if(comm=="exit")
                    {
						Console.WriteLine("Bye!");
						Thread.Sleep(500);
                        return;
                    }
                    if(ui.terminal.isScrolling())
                    {
                        ui.terminal.clearLines(infoBarHeight, Console.WindowHeight);
                        Console.SetCursorPosition(0, infoBarHeight);
                    }
                }
            }

            public string command(string comm)
            {
                switch (comm.Split(' ')[0])
                {
                    case "select":
                        if (comm.Split(' ').Length <= 1) return "Specify server";
                        if (!int.TryParse(comm.Split(' ')[1], out int n)) return comm.Split(' ')[1] + " is not a valid server id";
                        int t = Convert.ToInt32(comm.Split(' ')[1]);
                        if (!utilities.isInRange(0, selectedSession.serverList.Count, t)) return t + " is not a valid option";
                        selectedServer = t;
                        serverSelected = true;
                        return "OK";

                    case "return":
                        if (!serverSelected) return "Can't exit server view when server wasn't selected";
                        serverSelected = false;
                        return "OK";

                    case "task":
                        if(comm.Split(' ')[1].ToLower() != "all" && !int.TryParse(comm.Split(' ')[1], out int n2))
                            return comm.Split(' ')[1] + " is not a valid server id";

                        switch (comm.Split(' ')[2].ToLower())
                        {
                            case "add":
								Console.Write("Command to run>");
								string inp = Console.ReadLine();
								if (inp == "e") return "Aborted!";

                                if(comm.Split(' ')[1].ToLower() == "all")
                                {
                                    for (int i = 0; i < selectedSession.serverList.Count; i++)
                                    {
                                        selectedSession.serverList[i].submitTask(inp);
                                    }
                                } else {
                                    int taskServer = Convert.ToInt32(comm.Split(' ')[1]);
                                    selectedSession.serverList[taskServer].submitTask(inp);
                                }
                                return "OK";


                            default:
                                return "Unknown modifier: " + comm.Split(' ')[2].ToLower();
                        }
                        return "OK";
    				default:
                        return "";
                }
            }

            public void updateBridge(Object source, ElapsedEventArgs e)
            {
                drawInfoBar();
            }

            public void drawInfoBar()
            {
                if(!serverSelected)
                {
					ui.terminal.clearLines(0, infoBarHeight);
                    infoBarHeight = selectedSession.serverList.Count + 2;
                    for (int i = 0; i < selectedSession.serverList.Count; i++)
                    {
						server s = selectedSession.serverList[i];
						s.updateTasks();
						string stat = s.getStatus();
                        Console.Write(i);
						Console.Write(" [");
						if (stat == "Offline") Console.ForegroundColor = ConsoleColor.DarkRed;
						if (stat == "Idle") Console.ForegroundColor = ConsoleColor.DarkYellow;
						if (stat == "Running") Console.ForegroundColor = ConsoleColor.Green;
						Console.Write("*");
						Console.ResetColor();
						Console.Write("] " + utilities.ipFromSshAddr(s.sshAddress) + "(" + stat + ")");
                    }
                    Console.WriteLine("Last Updated: " + DateTime.Now.ToString("h:mm:ss tt"));
					ui.terminal.dividerBarCurrentLine();
                } else {
                    ui.terminal.clearLines(0, infoBarHeight);
                    infoBarHeight = selectedSession.serverList[selectedServer].taskList.Count + 5;
                    Console.WriteLine("Server(" + selectedServer + "): " + utilities.ipFromSshAddr(selectedSession.serverList[selectedServer].sshAddress));
                    Console.Write("Cpu: ");
                    ui.terminal.progressBar(selectedSession.serverList[selectedServer].getCpuPercent(), (int)Math.Round((double)Console.WindowWidth / 4));
                    Console.CursorTop++;
					Console.Write("Mem: ");
					ui.terminal.progressBar(selectedSession.serverList[selectedServer].getCpuPercent(), (int)Math.Round((double)Console.WindowWidth / 4));
                    Console.WriteLine("[Tasks]");
                    for (int i = 0; i < selectedSession.serverList[selectedServer].taskList.Count; i++)
                    {
                        Console.WriteLine("(" + i + ") " + selectedSession.serverList[selectedServer].taskList[i].comm 
                                          + " [" + selectedSession.serverList[selectedServer].taskList[i].status + "] PID: " 
                                         + selectedSession.serverList[selectedServer].taskList[i].lastPID);
                    }

					Console.WriteLine("Last Updated: " + DateTime.Now.ToString("h:mm:ss tt"));
					ui.terminal.dividerBarCurrentLine();
                }
            }
        }

        public static class utilities
        {
            static Ping ping = new Ping();

			static XmlSerializer serializer = new XmlSerializer(typeof(session));

            public static bool isOnline(string sshAddress)
            {
                List<string> pts = sshAddress.Split('@').ToList();
                pts.RemoveAt(0);
                string hostname = String.Join("@",pts);
                if (ping.Send(hostname).Status == IPStatus.Success) return true;
                return false;
            }

            public static string ipFromSshAddr(string sshAddress)
            {
				List<string> pts = sshAddress.Split('@').ToList();
				pts.RemoveAt(0);
				string hostname = String.Join("@", pts);
                return hostname;
            }

            public static string sshExec(string sshAddress, string command, session s, int port)
            {
                return exec(s.whichssh, "-p " + port + " " + sshAddress + " \"" + command + "\"");
            }

            public static string exec(string executable, string args)
            {
				Process process = new Process();
				process.StartInfo.FileName = executable;
                process.StartInfo.Arguments = args;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.Start();
				string output = process.StandardOutput.ReadToEnd();
				Console.WriteLine(output);
				string err = process.StandardError.ReadToEnd();
				Console.WriteLine(err);
				process.WaitForExit();
				return output;
            }

            public static session loadSessionXML(string xmlFile)
            {
                session result = null;

				using (FileStream fileStream = new FileStream(xmlFile, FileMode.Open))
				{
					result = (session)serializer.Deserialize(fileStream);
				}

                return result;
            }

            public static void saveSessionXML(string xmlFile, session sessionToSave)
            {
                if (File.Exists(xmlFile)) File.Delete(xmlFile);
				using (FileStream fileStream = new FileStream(xmlFile, FileMode.OpenOrCreate))
				{
					serializer.Serialize(fileStream, sessionToSave);
				}
            }

			public static bool isInRange(int l, int t, int i)
			{
				if (i > t) return false;
				if (l > i) return false;
				return true;
			}
        }

		public static class ui
		{
			public static int selectMenu1(string[] options, char[] _abreviations)
			{
                char[] abreviations = new char[_abreviations.Length];
                for (int i = 0; i < _abreviations.Length; i++)
                    abreviations[i] = Char.ToLower(_abreviations[i]);

                while (true)
				{
					Console.Write("Select ");
					for (int i = 0; i < options.Length; i++)
					{
						Console.Write("(" + _abreviations[i] + ")" + options[i].Substring(abreviations.Length - 1));
						if (i != options.Length - 1) Console.Write("/");
					}
					Console.Write("? ");
					char selected = Console.ReadKey().KeyChar;
					Console.WriteLine("");
					if (abreviations.Contains(selected)) return Array.IndexOf(abreviations, selected);
					Console.WriteLine(selected + " is not a valid option!");
				}
			}

			public static class terminal
			{
				public static void clearCurrentLine()
				{
					int currentLineCursor = Console.CursorTop;
					Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write(new string(' ', Console.WindowWidth));
					Console.SetCursorPosition(0, currentLineCursor);
				}

                public static void dividerBarCurrentLine()
                {
					int currentLineCursor = Console.CursorTop;
					Console.SetCursorPosition(0, Console.CursorTop);
					Console.Write(new string('=', Console.WindowWidth));
					Console.SetCursorPosition(0, currentLineCursor);
                }

                public static bool isScrolling()
                {
                    //Console.WriteLine(Console.CursorTop + ", " + (Console.WindowHeight-3));
                    return Console.CursorTop >= Console.WindowHeight-1;
                }

                public static void clearLines(int y1, int y2)
                {
                    int currentLineCursor = Console.CursorTop;
                    for (int i = y1; i < y2; i++)
                    {
                        Console.SetCursorPosition(0, i);
                        clearCurrentLine();
                    }
                    Console.SetCursorPosition(0, currentLineCursor);
                }

                public static void clearConsole()
                {
                    Console.SetCursorPosition(0, 0);
                    clearLines(0, Console.WindowHeight);
                }

                public static void progressBar(double percent, int width)
                {
                    //clearCurrentLine();
                    int screenAvail = width - (percent.ToString().Length + 3); //for each bracket, % sign and the double lenght
                    double scaleFactor = (double)screenAvail / 100;
                    int realVal = (int)Math.Round(percent * scaleFactor);
                    //Console.WriteLine(scaleFactor);
                    Console.Write("[" + new string('#', realVal) + new string(' ', screenAvail - realVal) + "]" + percent + "%");
                }
			}

            public static class updater
            {
                
            }
		}
    }
}
