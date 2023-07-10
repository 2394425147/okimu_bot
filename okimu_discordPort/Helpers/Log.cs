using System;

namespace okimu_discordPort.Helpers
{
    public static class Log
    {
        public static void Information(string text)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($@"[{now}] {text}");
        }
        
        public static void Error(string text, Exception e)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($@"[{now}] [ERROR] {text}\n" +
                              e);
        }
    }
}