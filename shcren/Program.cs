using System;

namespace shcren
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            //Console.WriteLine("Welcome to the shcren utility");
            //Console.WriteLine(Console.CursorLeft);
            shcrenCore.cliSession cli = new shcrenCore.cliSession();
            cli.start();
            Console.ReadLine();
        }
    }
}
