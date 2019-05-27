﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace Win10BloatRemover.Utils
{
    static class SystemUtils
    {
        public const string BACKUP_PRIVILEGE = "SeBackupPrivilege";
        public const string RESTORE_PRIVILEGE = "SeRestorePrivilege";
        public const string TAKE_OWNERSHIP_PRIVILEGE = "SeTakeOwnershipPrivilege";

        public static void GrantFullControlOnSubKey(this RegistryKey registryKey, string subkeyName)
        {
            GrantPrivilege(BACKUP_PRIVILEGE);
            GrantPrivilege(RESTORE_PRIVILEGE);
            GrantPrivilege(TAKE_OWNERSHIP_PRIVILEGE);

            RegistryKey subKey = registryKey.OpenSubKey(subkeyName,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.TakeOwnership | RegistryRights.ChangePermissions
            );

            if (subKey == null)
                throw new KeyNotFoundException($"Subkey {subkeyName} not found.");

            using (subKey)
            {
                RegistrySecurity accessRules = subKey.GetAccessControl();
                accessRules.SetOwner(WindowsIdentity.GetCurrent().User);
                accessRules.ResetAccessRule(
                    new RegistryAccessRule(
                        WindowsIdentity.GetCurrent().User,
                        RegistryRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow
                    )
                );
                subKey.SetAccessControl(accessRules);
            }

            RevokePrivilege(TAKE_OWNERSHIP_PRIVILEGE);
            RevokePrivilege(BACKUP_PRIVILEGE);
            RevokePrivilege(RESTORE_PRIVILEGE);
        }

        public static void GrantPrivilege(string privilege)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref tokenHandle);
            TokenPrivileges tokenPrivilege = new TokenPrivileges {
                Count = 1,
                LUID = 0,
                Attributes = SE_PRIVILEGE_ENABLED
            };
            LookupPrivilegeValue(null, privilege, ref tokenPrivilege.LUID);
            AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivilege, 0, IntPtr.Zero, IntPtr.Zero);
        }

        public static void RevokePrivilege(string privilege)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref tokenHandle);
            TokenPrivileges tokenPrivilege = new TokenPrivileges {
                Count = 1,
                LUID = 0,
                Attributes = SE_PRIVILEGE_DISABLED
            };
            LookupPrivilegeValue(null, privilege, ref tokenPrivilege.LUID);
            AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivilege, 0, IntPtr.Zero, IntPtr.Zero);
        }

        // This method locks the thread until the process stops writing on those streams (usually until its end)
        public static void PrintSynchronouslyOutputAndErrors(this Process process)
        {
            Console.Write(process.StandardOutput.ReadToEnd());
            ConsoleUtils.Write(process.StandardError.ReadToEnd(), ConsoleColor.Red);
        }

        public static Process RunProcess(string name, string args)
        {
            var process = CreateProcessInstance(name, args);
            process.Start();
            return process;
        }

        public static Process RunProcessWithAsyncOutputPrinting(string name, string args)
        {
            var process = CreateProcessInstance(name, args);

            process.OutputDataReceived += (_, evt) => {
                if (!string.IsNullOrEmpty(evt.Data))
                    Console.WriteLine(evt.Data);
            };
            process.ErrorDataReceived += (_, evt) => {
                if (!string.IsNullOrEmpty(evt.Data))
                    ConsoleUtils.WriteLine(evt.Data, ConsoleColor.Red);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static Process CreateProcessInstance(string name, string args)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
        }

        /**
         *  Deletes the folder passed recursively
         *  Can optionally handle exceptions inside its body to avoid propagating of errors
         */
        public static void DeleteDirectoryIfExists(string path, bool handleErrors = false)
        {
            if (handleErrors)
            {
                try
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                }
                catch (Exception exc)
                {
                    ConsoleUtils.WriteLine($"An error occurred when deleting folder {path}: {exc.Message}", ConsoleColor.Red);
                }
            }
            else
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }

        public static bool IsWindowsReleaseId(string expectedId)
        {
            string releaseId = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                                                  "ReleaseId", "").ToString();
            return releaseId == expectedId;
        }

        public static bool HasAdministratorRights()
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TokenPrivileges
        {
            public int Count;
            public long LUID;
            public int Attributes;
        }

        private const int SE_PRIVILEGE_DISABLED = 0x00000000;
        private const int SE_PRIVILEGE_ENABLED = 0x00000002;
        private const int TOKEN_QUERY = 0x00000008;
        private const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle,
                                                         bool disableAllPrivileges,
                                                         ref TokenPrivileges newState,
                                                         int bufferLength,
                                                         IntPtr previousState,
                                                         IntPtr returnLength);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, ref IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string systemName, string privilegeName, ref long privilegeLUID);
    }
}