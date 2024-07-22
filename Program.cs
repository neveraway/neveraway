using System;
using System.Windows.Forms;

namespace NeverAway;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        Application.Run(new NeverAwayApp());
    }
}