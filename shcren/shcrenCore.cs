using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Xml.Serialization;
using System.Threading;

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

            public cliSession()
            {
                bool skipLoad = false;
                Console.WriteLine("Welcome to the shcrenCore CLI version {0}", version);
                if(!File.Exists("currentSession.xml"))
                {
                    Console.WriteLine("No session found!");
                    int option = ui.selectMenu1(new string[] {"Create", "Exit"}, new char[] {'C', 'E'});

                    if(option == 0)
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

                if(!skipLoad)
                {
                    Console.WriteLine("Loading last session");
                    selectedSession = utilities.loadSessionXML("currentSession.xml");
                }

                terminal();
            }

            public void terminal()
            {
                ui.terminal.reset();
                //Draw top server info bar
                //draw border
                //Draw command window
            }

            public void command(string comm)
            {
                switch (comm)
                {
                    default:
                        break;
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
        }

		public static class ui
		{
			public static int selectMenu1(string[] options, char[] abreviations)
			{
				while (true)
				{
					terminal.write("Select ");
					for (int i = 0; i < options.Length; i++)
					{
						terminal.write("(" + abreviations[i] + ")" + options[i].Substring(abreviations.Length - 1));
						if (i != options.Length - 1) terminal.write("/");
					}
					terminal.write("? ");
					char selected = Console.ReadKey().KeyChar;
					terminal.writeline("");
					if (abreviations.Contains(selected)) return Array.IndexOf(abreviations, selected);
					terminal.writeline(selected + " is not a valid option!");
				}
			}

			public static class terminal
			{
				public static int currentxPos = 0;
				public static int currentyPos = 0;
				public static int swapx = 0;
				public static int swapy = 0;
                public static bool isSwaped = false;

				public static void ClearCurrentConsoleLine()
				{
					int currentLineCursor = Console.CursorTop;
					Console.SetCursorPosition(0, Console.CursorTop);
					Console.Write(new string(' ', Console.WindowWidth));
					Console.SetCursorPosition(0, currentLineCursor);
				}

				public static void reset()
				{
					Console.Clear();
					currentxPos = 0;
					currentyPos = 0;
				}

				public static void fullReset()
				{
					reset();
					swapx = currentxPos;
					swapy = currentyPos;
				}

				public static void write(string str)
				{
					Console.Write(str);
				}

				public static void writeline(string str)
				{
					Console.WriteLine(str);
                    if(isSwaped)
                        swapy++;
                    if (!isSwaped)
                        currentyPos++;
				}

                public static void swap()
                {
                    if(isSwaped)
                    {
                        swapy = Console.CursorTop;
                        swapx = Console.CursorLeft;
                        Console.SetCursorPosition(currentxPos, currentyPos);
                    } else {
						currentyPos = Console.CursorTop;
						currentxPos = Console.CursorLeft;
                        Console.SetCursorPosition(swapx, swapy);
                    }
                    isSwaped = !isSwaped;
                }

                public static bool isScrolling()
                {
                    return Console.CursorTop >= Console.WindowHeight;
                }
			}
		}
    }
}
