using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.MobileImageMounter;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MobileDevice.Demo
{
    class Program
    {
        private static string PathForImage(string iOSVersion)
        {
            return $"drivers/{iOSVersion}/inject.dmg";
        }

        private static string PathForImageSign(string iOSVersion)
        {
            return $"drivers/{iOSVersion}/inject.dmg.signature";
        }
        static void Main(string[] args)
        {
            Console.WriteLine("Starting the imobiledevice-net demo");

            // This demo application will use the libimobiledevice API to list all iOS devices currently
            // connected to this PC.

            // First, we need to make sure the unmanaged (native) libimobiledevice libraries are loaded correctly
            NativeLibraries.Load();

            ReadOnlyCollection<string> devices;
            int count = 0;

            var idevice = LibiMobileDevice.Instance.iDevice;
            var lockdown = LibiMobileDevice.Instance.Lockdown;
            var screenshotr = LibiMobileDevice.Instance.Screenshotr;
            var service = LibiMobileDevice.Instance.Service;


            var ret = idevice.idevice_get_device_list(out devices, ref count);

            if (ret == iDeviceError.NoDevice)
            {
                // Not actually an error in our case
                Console.WriteLine("No devices found");
                return;
            }

            ret.ThrowOnError();

            // Get the device name
            foreach (var device in devices)
            {   
                Console.WriteLine(device);
                
                iDeviceHandle deviceHandle;
                idevice.idevice_new(out deviceHandle, device).ThrowOnError();

                LockdownClientHandle lockdownHandle;

                lockdown.lockdownd_client_new_with_handshake(deviceHandle, out lockdownHandle, "location").ThrowOnError();

                string deviceName;
                lockdown.lockdownd_get_device_name(lockdownHandle, out deviceName).ThrowOnError();

                Console.WriteLine($"{deviceName} ({device})");


                deviceHandle.Dispose();
                lockdownHandle.Dispose();
            }
        }

        private static void DoProcess(string shandle, string lat, string lon)
        {
            var iDevice = iMobileDevice.LibiMobileDevice.Instance.iDevice;
            var lockdown = iMobileDevice.LibiMobileDevice.Instance.Lockdown;
            var service = iMobileDevice.LibiMobileDevice.Instance.Service;

            var basePath = $"win-x{(Environment.Is64BitProcess ? "64" : "86")}/";

            Console.WriteLine("idevice_new");

            var error = iDevice.idevice_new(out var iDeviceHandle, shandle);
            if (error != iDeviceError.Success)
            {
                Console.WriteLine(error);
                return;
            }
            using (iDeviceHandle)
            {
                Console.WriteLine("lockdownd_client_new");
                var lockdownError = lockdown.lockdownd_client_new_with_handshake(iDeviceHandle, out var lockdownClientHandle, "com.alpha.jailout." + Guid.NewGuid().ToString("N"));
                if (lockdownError != LockdownError.Success)
                {
                    Console.WriteLine(lockdownError.ToString());
                    return;
                }
                using (lockdownClientHandle)
                {
                    string iOSVersion = null;
                    Console.WriteLine("mount development image");
                    lockdownError = lockdown.lockdownd_get_value(lockdownClientHandle, null, "ProductVersion", out var plistHandle);
                    if (lockdownError != LockdownError.Success)
                    {
                        Console.WriteLine("get iOS version error: " + lockdownError.ToString());
                    }
                    else
                    {
                        using (plistHandle)
                        {
                            iMobileDevice.LibiMobileDevice.Instance.Plist.plist_get_string_val(plistHandle, out iOSVersion);
                            Console.WriteLine("iOS: " + iOSVersion);
                            if (!File.Exists(PathForImage(iOSVersion)) || !File.Exists(PathForImageSign(iOSVersion)))
                            {
                                iOSVersion = Regex.Replace(iOSVersion, @"^(\d+.\d+).*$", "$1");
                            }
                            if (!File.Exists(PathForImage(iOSVersion)) || !File.Exists(PathForImageSign(iOSVersion)))
                            {
                                Console.WriteLine($"can not found {iOSVersion} driver");
                                return;
                            }
                            //
                            // var process = Process.Start(new ProcessStartInfo()
                            // {
                            //     FileName = $"{basePath}ideviceimagemounter.exe",
                            //     Arguments = $"-u {shandle} drivers/{iOSVersion}/inject.dmg drivers/{iOSVersion}/inject.dmg.signature",
                            //     RedirectStandardOutput = true,
                            //     RedirectStandardError = true,
                            //     UseShellExecute = false,
                            // });
                            // process.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
                            // process.ErrorDataReceived += (sender, args) => Console.WriteLine(args.Data);
                            // process.EnableRaisingEvents = true;
                            // process.BeginOutputReadLine();
                            // process.BeginErrorReadLine();
                            // process.WaitForExit();
                        }
                    }
                    if (iOSVersion != null)
                    {
                        if (!MountDevelopmentImage(iDeviceHandle, lockdownClientHandle, iOSVersion))
                        {
                            Console.WriteLine("mount failed.");
                            return;
                        }
                    }
                    Console.WriteLine("start com.apple.dt.simulatelocation");
                    lockdownError = lockdown.lockdownd_start_service(lockdownClientHandle, "com.apple.dt.simulatelocation", out var lockdownHandle);
                    if (lockdownError != LockdownError.Success)
                    {
                        Console.WriteLine(lockdownError.ToString());
                        return;
                    }
                    using (lockdownHandle)
                    {
                        Console.WriteLine("service_client_new");
                        var serviceError = service.service_client_new(iDeviceHandle, lockdownHandle, out var serviceClientHandle);
                        if (serviceError != ServiceError.Success)
                        {
                            Console.WriteLine(serviceError.ToString());
                            return;
                        }
                        using (serviceClientHandle)
                        {
                            if (string.IsNullOrWhiteSpace(lat) || string.IsNullOrWhiteSpace(lon))
                            {
                                Restore(serviceClientHandle);
                            }
                            else
                            {
                                SendLocation(serviceClientHandle, lat, lon);
                            }
                        }
                    }
                }
            }
        }

        private static bool MountDevelopmentImage(iDeviceHandle iDeviceHandle, LockdownClientHandle lockdownClientHandle, string iOsVersion)
        {
            var mounter = iMobileDevice.LibiMobileDevice.Instance.MobileImageMounter;
            var lockdown = iMobileDevice.LibiMobileDevice.Instance.Lockdown;
            var plist = iMobileDevice.LibiMobileDevice.Instance.Plist;

            Console.WriteLine("start com.apple.mobile.mobile_image_mounter");
            var lockdownError = lockdown.lockdownd_start_service(lockdownClientHandle, "com.apple.mobile.mobile_image_mounter", out var lockdownHandle);
            if (lockdownError != LockdownError.Success)
            {
                Console.WriteLine(lockdownError.ToString());
                return false;
            }
            using (lockdownHandle)
            {
                var mounterError = mounter.mobile_image_mounter_new(iDeviceHandle, lockdownHandle, out var mounterClientHandle);
                if (mounterError != MobileImageMounterError.Success)
                {
                    Console.WriteLine("connect to com.apple.mobile.mobile_image_mounter failed.");
                    return false;
                }
                using (mounterClientHandle)
                {
                    try
                    {
                        mounterError = mounter.mobile_image_mounter_lookup_image(mounterClientHandle, "Developer", out var plistHandle);
                        if (mounterError != MobileImageMounterError.Success)
                        {
                            Console.WriteLine("lookup_image failed: " + mounterError);
                            return false;
                        }
                        using (plistHandle)
                        {
                            var arr = plist.plist_dict_get_item(plistHandle, "ImageSignature");
                            using (arr)
                            {
                                var size = plist.plist_array_get_size(arr);
                                if (size > 0)
                                {
                                    Console.WriteLine("mounted, skip.");
                                    return true;
                                }
                            }
                        }
                        var image = File.ReadAllBytes(PathForImage(iOsVersion));
                        var imageSign = File.ReadAllBytes(PathForImageSign(iOsVersion));
                        var i = 0;
                        mounterError = mounter.mobile_image_mounter_upload_image(mounterClientHandle, "Developer", (uint)image.Length, imageSign, (ushort)imageSign.Length,
                            ((buffer, length, data) =>
                            {
                                // Console.WriteLine($"{buffer.ToString("X")} {i} {length} {data.ToString("X")}");
                                Marshal.Copy(image, i, buffer, (int)length);
                                i += (int)length;
                                return (int)length;
                            }), new IntPtr(0));
                        if (mounterError != MobileImageMounterError.Success)
                        {
                            Console.WriteLine("upload_image failed: " + mounterError);
                            return false;
                        }
                        Console.WriteLine("upload_image done");

                        mounterError = mounter.mobile_image_mounter_mount_image(mounterClientHandle, "/private/var/mobile/Media/PublicStaging/staging.dimage", imageSign,
                            (ushort)imageSign.Length, "Developer", out plistHandle);
                        if (mounterError != MobileImageMounterError.Success)
                        {
                            Console.WriteLine("mount_image failed: " + mounterError);
                            return false;
                        }
                        using (plistHandle)
                        {
                            uint slen = 0;
                            plist.plist_to_xml(plistHandle, out var result, ref slen);
                            if (!result.Contains("Complete") || result.Contains("ImageMountFailed"))
                            {
                                Console.WriteLine("mount_image failed: " + result);
                                return false;
                            }
                        }
                        Console.WriteLine("mount_image done");
                    }
                    finally
                    {
                        if (mounterClientHandle != null && !mounterClientHandle.IsClosed)
                        {
                            mounterError = mounter.mobile_image_mounter_hangup(mounterClientHandle);
                            if (mounterError != MobileImageMounterError.Success)
                            {
                                Console.WriteLine("hangup failed: " + mounterError);
                            }
                        }
                    }
                }
            }
            return true;
        }

    }
}