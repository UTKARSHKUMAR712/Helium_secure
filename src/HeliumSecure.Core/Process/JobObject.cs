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

    private const int JobObjectBasicProcessIdList = 3;

    private nint _handle;
    private bool _disposed;

    public JobObject(string? name = null)
    {
        _handle = CreateJobObject(nint.Zero, name);
        if (_handle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Job Object.");
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

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != nint.Zero)
            {
                CloseHandle(_handle);
                _handle = nint.Zero;
            }
            _disposed = true;
        }
    }
}
