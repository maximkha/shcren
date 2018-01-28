using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Linq;
using System.Diagnostics;

namespace shcren
{
    public static class shcrenCore
    {
        public class task
        {
            public int lastPID;
            public string comm;
            public string status;
        }

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
                if (taskList.All(x=>x.status=="Done")) return "Idle";
                if (taskList.Count > 0) return "Running " + taskList.Count + "tasks";
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
                    if(!runningPids.Contains(taskList[i].lastPID)) taskList.RemoveAt(i);
            }

            public string query(string command)
            {
                return utilities.sshExec(sshAddress, command, parentSession, port);
            }

            public bool copyFile(string filename, string destinationDirectory)
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
        }

        public class session
        {
            public string whichssh = "/usr/bin/ssh";
            public string whichtar = "/usr/bin/tar";
            public string whichbash = "/bin/bash";
        }

        public static class utilities
        {
            static Ping ping = new Ping();

            public static bool isOnline(string sshAddress)
            {
                List<string> pts = sshAddress.Split('@').ToList();
                pts.RemoveAt(0);
                string hostname = String.Join("@",pts);
                if (ping.Send(hostname).Status == IPStatus.Success) return true;
                return false;
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
        }
    }
}
