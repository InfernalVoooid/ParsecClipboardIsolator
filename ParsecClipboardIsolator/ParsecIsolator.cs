using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace ParsecClipboardIsolator
{
    internal sealed class ParsecIsolator : IDisposable
    {
        private const string TargetProcessName = "parsecd";
        private static readonly byte[] PatchBytes = [0x33, 0xC0, 0xC3]; // xor eax, eax; ret
        
        private readonly Dictionary<int, IntPtr> _hProcesses = [];
        private readonly byte[] _originalBytes = new byte[3];
        private IntPtr _openClipboardAddr;

        public bool IsActive { get; private set; }
        public int TrackedProcessesCount => _hProcesses.Count;

        public record RefreshResult(int NewlyAttached, int SkippedDueToArch, int Removed);

        public RefreshResult Initialize()
        {
            // Адрес OpenClipboard в системной user32.dll одинаков для всех x64 процессов в сессии.
            IntPtr user32 = Native.LoadLibrary("user32.dll");
            if (user32 == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось загрузить user32.dll.");

            _openClipboardAddr = Native.GetProcAddress(user32, "OpenClipboard");
            if (_openClipboardAddr == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось найти адрес функции OpenClipboard.");

            Marshal.Copy(_openClipboardAddr, _originalBytes, 0, 3);

            SetBlockState(true);

            return Refresh();
        }

        public RefreshResult Refresh()
        {
            var processes = Process.GetProcessesByName(TargetProcessName);
            
            if (processes.Length == 0 && _hProcesses.Count == 0) 
                return new RefreshResult(0, 0, 0);

            int newlyAttached = 0;
            int skippedDueToArch = 0;
            int removed = 0;

            var activePids = processes.Select(p => p.Id).ToHashSet();
            var pidsToRemove = _hProcesses.Keys.Where(pid => !activePids.Contains(pid)).ToList();

            foreach (var pid in pidsToRemove)
            {
                Native.CloseHandle(_hProcesses[pid]);
                _hProcesses.Remove(pid);
                removed++;
            }

            foreach (var p in processes)
            {
                if (_hProcesses.ContainsKey(p.Id)) continue; 

                IntPtr hProc = Native.OpenProcess(
                    Native.PROCESS_VM_WRITE | Native.PROCESS_VM_OPERATION | Native.PROCESS_QUERY_LIMITED_INFORMATION, 
                    false, 
                    p.Id);
                
                if (hProc == IntPtr.Zero) continue;

                if (!IsArchitectureCompatible(hProc))
                {
                    Native.CloseHandle(hProc);
                    skippedDueToArch++;
                    continue;
                }

                _hProcesses.Add(p.Id, hProc);
                newlyAttached++;
                
                if (IsActive)
                {
                    WriteMemory(hProc, PatchBytes);
                }
            }

            return new RefreshResult(newlyAttached, skippedDueToArch, removed);
        }

        public void SetBlockState(bool block)
        {
            IsActive = block; 
            if (_hProcesses.Count == 0) return;

            ReadOnlySpan<byte> dataToWrite = block ? PatchBytes : _originalBytes;

            foreach (var hProc in _hProcesses.Values)
            {
                WriteMemory(hProc, dataToWrite);
            }
        }

        public void Dispose()
        {
            if (IsActive) SetBlockState(false);

            foreach (var hProc in _hProcesses.Values)
            {
                Native.CloseHandle(hProc);
            }
            _hProcesses.Clear();
        }

        private bool IsArchitectureCompatible(IntPtr hProc)
        {
            if (!Native.IsWow64Process(hProc, out bool isTargetWow64)) return true; // При ошибке вызова предполагаем совместимость

            bool isTarget32Bit = Environment.Is64BitOperatingSystem ? isTargetWow64 : true;
            bool isOurApp32Bit = !Environment.Is64BitProcess;

            return isTarget32Bit == isOurApp32Bit;
        }

        private void WriteMemory(IntPtr hProc, ReadOnlySpan<byte> data)
        {
            if (Native.VirtualProtectEx(hProc, _openClipboardAddr, (UIntPtr)data.Length, Native.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                Native.WriteProcessMemory(hProc, _openClipboardAddr, in MemoryMarshal.GetReference(data), data.Length, out _);
                Native.VirtualProtectEx(hProc, _openClipboardAddr, (UIntPtr)data.Length, oldProtect, out _);
            }
        }
    }
}
