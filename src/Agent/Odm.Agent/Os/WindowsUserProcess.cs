using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Odm.Agent.Os;

/// <summary>
/// Starts a program in the session of the user signed in at the console. The agent runs as SYSTEM, so it can
/// obtain that user's token (WTSQueryUserToken). Output goes to a temporary file whose handle is inherited,
/// so no pipes are needed. Not testable without a Windows session; the pure parts are in <see cref="UserSessions"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsUserProcess
{
    private const uint NoSession = 0xFFFFFFFF;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint WaitTimeout = 0x00000102;
    private const uint TokenAllAccess = 0xF01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    public static async Task<ProcessRunner.Result> RunAsync(
        string tool, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == NoSession || !WTSQueryUserToken(session, out var userToken))
        {
            throw new UserContextException("No user is signed in at the console of this device.");
        }

        IntPtr primary = IntPtr.Zero, environment = IntPtr.Zero, output = IntPtr.Zero, input = IntPtr.Zero;
        var outputFile = Path.Combine(Path.GetTempPath(), $"odm-user-{Guid.NewGuid():N}.log");
        try
        {
            if (!DuplicateTokenEx(userToken, TokenAllAccess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary))
            {
                throw new UserContextException($"Token of the signed-in user not available: {new Win32Exception().Message}");
            }

            if (!CreateEnvironmentBlock(out environment, primary, false))
            {
                throw new UserContextException($"Environment of the signed-in user not available: {new Win32Exception().Message}");
            }

            var file = Resolve(tool, environment);
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
            output = CreateFile(outputFile, 0x40000000, 3, ref attributes, 2, 0x80, IntPtr.Zero);
            input = CreateFile("NUL", 0x80000000, 3, ref attributes, 3, 0x80, IntPtr.Zero);
            if (output == new IntPtr(-1) || input == new IntPtr(-1))
            {
                throw new UserContextException($"Output file could not be created: {new Win32Exception().Message}");
            }

            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = @"winsta0\default",
                Flags = StartfUseStdHandles,
                StdInput = input,
                StdOutput = output,
                StdError = output,
            };
            var commandLine = new StringBuilder(UserSessions.QuoteWindowsArgument(file) + " " + UserSessions.QuoteWindowsArguments(arguments));

            if (!CreateProcessAsUser(
                    primary, null, commandLine, IntPtr.Zero, IntPtr.Zero, true, CreateUnicodeEnvironment | CreateNoWindow,
                    environment, null, ref startup, out var process))
            {
                throw new UserContextException($"Start in the user session failed: {new Win32Exception().Message}");
            }

            try
            {
                var timedOut = await Task.Run(() => WaitFor(process.Process, timeout, ct), CancellationToken.None);
                if (timedOut || ct.IsCancellationRequested)
                {
                    TerminateProcess(process.Process, 1);
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
                }

                GetExitCodeProcess(process.Process, out var exitCode);
                CloseHandle(output);
                output = IntPtr.Zero;
                return new ProcessRunner.Result(timedOut ? -1 : unchecked((int)exitCode), ReadTail(outputFile), "", timedOut);
            }
            finally
            {
                CloseHandle(process.Process);
                CloseHandle(process.Thread);
            }
        }
        finally
        {
            if (output != IntPtr.Zero) CloseHandle(output);
            if (input != IntPtr.Zero && input != new IntPtr(-1)) CloseHandle(input);
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primary != IntPtr.Zero) CloseHandle(primary);
            CloseHandle(userToken);
            try { File.Delete(outputFile); } catch (IOException) { /* temporary file, cleaned up later */ }
        }
    }

    // Polls so that cancellation is noticed; returns true on timeout.
    private static bool WaitFor(IntPtr process, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!ct.IsCancellationRequested)
        {
            if (WaitForSingleObject(process, 500) != WaitTimeout)
            {
                return false;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadTail(string path)
    {
        const int Max = 1_000_000;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > Max)
        {
            stream.Seek(-Max, SeekOrigin.End);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // "winget" lives in the user's WindowsApps alias folder; other tools are given as a full path.
    private static string Resolve(string tool, IntPtr environment)
    {
        if (Path.IsPathRooted(tool))
        {
            return tool;
        }

        var variables = ReadEnvironment(environment);
        if (variables.TryGetValue("LOCALAPPDATA", out var local))
        {
            var candidate = Path.Combine(local, "Microsoft", "WindowsApps", tool + ".exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new UserContextException($"{tool} was not found for the signed-in user (App Installer missing?).");
    }

    private static Dictionary<string, string> ReadEnvironment(IntPtr block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cursor = block;
        while (true)
        {
            var entry = Marshal.PtrToStringUni(cursor) ?? "";
            if (entry.Length == 0)
            {
                return result;
            }

            var separator = entry.IndexOf('=', 1);
            if (separator > 0)
            {
                result[entry[..separator]] = entry[(separator + 1)..];
            }

            cursor += (entry.Length + 1) * 2;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public uint Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved3;
        public IntPtr StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string? directory,
        ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string fileName, uint access, uint share, ref SecurityAttributes attributes, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
