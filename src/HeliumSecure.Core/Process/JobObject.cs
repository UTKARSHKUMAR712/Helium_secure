using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeliumSecure.Core.Process;

public sealed class JobObject : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        nint hJob,
        int JobObjectInformationClass,
        nint lpJobObjectInformation,
        uint cbJobObjectInformationLength,
        out uint lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint hJob,
        int JobObjectInformationClass,
        nint lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(nint hJob, uint uExitCode);

    private const int JobObjectBasicProcessIdList = 3;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryLimit;
        public nuint PeakJobMemoryLimit;
    }

    private nint _handle;
    private bool _disposed;

    public JobObject(string? name = null)
    {
        _handle = CreateJobObject(nint.Zero, name);
        if (_handle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Job Object.");
        }

        SetKillOnClose();
    }

    private void SetKillOnClose()
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        nint pInfo = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, pInfo, false);
            SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, pInfo, (uint)length);
        }
        finally
        {
            Marshal.FreeHGlobal(pInfo);
        }
    }

    public bool AssignProcess(System.Diagnostics.Process process)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            if (process.HasExited) return false;
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    public int GetActiveProcessCount()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint bufferSize = 4096;
        nint buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (QueryInformationJobObject(_handle, JobObjectBasicProcessIdList, buffer, bufferSize, out _))
            {
                // First field of JOBOBJECT_BASIC_PROCESS_ID_LIST is NumberOfAssignedProcesses (DWORD),
                // second field is NumberOfProcessIdsInList (DWORD)
                int numberOfProcesses = Marshal.ReadInt32(buffer + 4);
                return numberOfProcesses;
            }
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Terminate(uint exitCode = 0)
    {
        if (_handle != nint.Zero)
        {
            try
            {
                TerminateJobObject(_handle, exitCode);
            }
            catch
            {
                // Best effort
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != nint.Zero)
            {
                Terminate(0);
                CloseHandle(_handle);
                _handle = nint.Zero;
            }
            _disposed = true;
        }
    }
}
