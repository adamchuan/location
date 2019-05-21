using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.Service;
using iMobileDevice.MobileImageMounter;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Location
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

                Console.Write(device);
                string lat = "113.970076";
                string lon = "22.533695";

                double fLat = Convert.ToDouble(lat);
                double fLon = Convert.ToDouble(lon);

                DoProcess(device, fLat.ToString(), fLon.ToString());
            }
        }

        private static void DoProcess(string shandle, string lat, string lon)
        {
            var iDevice = iMobileDevice.LibiMobileDevice.Instance.iDevice;
            var lockdown = iMobileDevice.LibiMobileDevice.Instance.Lockdown;
            var service = iMobileDevice.LibiMobileDevice.Instance.Service;
            var mounter = iMobileDevice.LibiMobileDevice.Instance.MobileImageMounter;

            var basePath = $"win-x{(Environment.Is64BitProcess ? "64" : "86")}/";

            Console.WriteLine("idevice_new");

            var error = iDevice.idevice_new(out var iDeviceHandle, shandle);
            if (error != iDeviceError.Success)
            {
                Console.WriteLine(error);
                return;
            }

            Console.WriteLine("lockdownd_client_new");
            lockdown.lockdownd_client_new_with_handshake(iDeviceHandle, out var lockdownClientHandle, "kwdxh." + Guid.NewGuid().ToString("N")).ThrowOnError("connect error");

            string iOSVersion = GetIOSVerison(lockdownClientHandle);
            Console.WriteLine("mount development image");

            if (!File.Exists(PathForImage(iOSVersion)) || !File.Exists(PathForImageSign(iOSVersion)))
            {
                iOSVersion = Regex.Replace(iOSVersion, @"^(\d+.\d+).*$", "$1"); // 只取到 minor 版本 ，如 12.2
            }
            if (!File.Exists(PathForImage(iOSVersion)) || !File.Exists(PathForImageSign(iOSVersion)))
            {
                Console.WriteLine($"can not found {iOSVersion} driver");
                return;
            }

            MobileImageMounterClientHandle mobileImageMounterClientHandle;

            MountDevelopmentImage(iDeviceHandle, lockdownClientHandle, iOSVersion, out mobileImageMounterClientHandle);


            double fLat = Convert.ToDouble(lat);
            double fLon = Convert.ToDouble(lon);
            
            while (true)
            {   
                try {

                    lockdown.lockdownd_client_new_with_handshake(iDeviceHandle, out var lockdownClientHandle2, "kwdxh").ThrowOnError("握手失败");

                    LockdownServiceDescriptorHandle lockdownServiceDescriptorHandle;
                    Console.WriteLine("start server");
                    
                    lockdown.lockdownd_start_service(lockdownClientHandle2, "com.apple.dt.simulatelocation", out lockdownServiceDescriptorHandle).ThrowOnError("虚拟定位服务开启失败1");

                    Console.WriteLine("start server client");
                    service.service_client_new(iDeviceHandle, lockdownServiceDescriptorHandle, out var serviceClientHandle).ThrowOnError("虚拟定位服务开启失败2");


                    if (string.IsNullOrWhiteSpace(lat) || string.IsNullOrWhiteSpace(lon))
                    {
                        Restore(serviceClientHandle);
                        break;
                    }
                    else
                    {

                        SendLocation(serviceClientHandle, fLat.ToString(), fLon.ToString());
                    }
                    serviceClientHandle.Close();
                    serviceClientHandle.Dispose();
                    lockdownServiceDescriptorHandle.Close();
                    lockdownServiceDescriptorHandle.Dispose();

                    string str = (string)Console.ReadLine(); // get a char
                    var ESC = false;
                    
                    for(var i = 0; i < str.Length; i++) {
                        var ch = str[i];

                        switch (ch)
                        {
                            case 's':
                                fLon -= 0.0003;
                                break;
                            case 'w':
                                fLon += 0.0003;
                                break;
                            case 'a':
                                fLat -= 0.0005;
                                break;
                            case 'd':
                                fLat += 0.0005;
                                break;
                            case 'q':
                                ESC = true;
                                break;
                            default:
                                continue;
                        }
                    }

                    if (ESC)
                    {
                        break;
                    }
                } catch {
                    Console.Write("error");
                }
            }

            mobileImageMounterClientHandle.Close();
            mobileImageMounterClientHandle.Dispose();
        
            
            lockdownClientHandle.Dispose();
            iDeviceHandle.Dispose();

            RecoverDevice(shandle);
        }

    
        private static string GetIOSVerison(LockdownClientHandle lockdownClientHandle)
        {
            string iOSVersion = null;

            var lockdown = iMobileDevice.LibiMobileDevice.Instance.Lockdown;
            var plist = iMobileDevice.LibiMobileDevice.Instance.Plist;

            var lockdownError = lockdown.lockdownd_get_value(lockdownClientHandle, null, "ProductVersion", out var plistHandle);
            if (lockdownError != LockdownError.Success)
            {
                Console.WriteLine("get iOS version error: " + lockdownError.ToString());
            }
            else
            {
                using (plistHandle)
                {
                    plist.plist_get_string_val(plistHandle, out iOSVersion);
                }
            }

            return iOSVersion;
        }
        private static bool MountDevelopmentImage(iDeviceHandle iDeviceHandle, LockdownClientHandle lockdownClientHandle, string iOsVersion, out MobileImageMounterClientHandle mobileImageMounterClientHandle)
        {
            var mounter = iMobileDevice.LibiMobileDevice.Instance.MobileImageMounter;
            var lockdown = iMobileDevice.LibiMobileDevice.Instance.Lockdown;
            var plist = iMobileDevice.LibiMobileDevice.Instance.Plist;

            mobileImageMounterClientHandle = null;
            Console.WriteLine("start com.apple.mobile.mobile_image_mounter");
            var lockdownError = lockdown.lockdownd_start_service(lockdownClientHandle, "com.apple.mobile.mobile_image_mounter", out var lockdownHandle);
            if (lockdownError != LockdownError.Success)
            {
                Console.WriteLine(lockdownError.ToString());
                return false;
            }
            using (lockdownHandle)
            {
                var mounterError = mounter.mobile_image_mounter_new(iDeviceHandle, lockdownHandle, out mobileImageMounterClientHandle);
                if (mounterError != MobileImageMounterError.Success)
                {
                    Console.WriteLine("connect to com.apple.mobile.mobile_image_mounter failed.");
                    return false;
                }
                
                using (mobileImageMounterClientHandle)
                {
                    try
                    {
                        mounterError = mounter.mobile_image_mounter_lookup_image(mobileImageMounterClientHandle, "Developer", out var plistHandle);
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

                        Console.WriteLine(image.Length);
                        Console.WriteLine(imageSign.Length);
                        var i = 0;
                        mounterError = mounter.mobile_image_mounter_upload_image(mobileImageMounterClientHandle, "Developer", (uint)image.Length, imageSign, (ushort)imageSign.Length,
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

                        mounterError = mounter.mobile_image_mounter_mount_image(mobileImageMounterClientHandle, "/private/var/mobile/Media/PublicStaging/staging.dimage", imageSign,
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
                        if (mobileImageMounterClientHandle != null && !mobileImageMounterClientHandle.IsClosed)
                        {
                            mounterError = mounter.mobile_image_mounter_hangup(mobileImageMounterClientHandle);
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
        private static void SendLocation(ServiceClientHandle serviceClientHandle, string lat, string lon)
        {
            Console.WriteLine($"SendLocation {lat},{lon}");
            var serviceError = SendCmd(serviceClientHandle, 0);
            if (serviceError != ServiceError.Success)
            {
                return;
            }
            serviceError = SendString(serviceClientHandle, lon);
            if (serviceError != ServiceError.Success)
            {
                return;
            }
            serviceError = SendString(serviceClientHandle, lat);
            if (serviceError != ServiceError.Success)
            {
                Console.WriteLine("SendLocation failed");
                return;
            }
            Console.WriteLine("SendLocation success");
        }

        private static ServiceError SendString(ServiceClientHandle serviceClientHandle, string str)
        {
            var service = iMobileDevice.LibiMobileDevice.Instance.Service;
            uint num2 = 0;
            var sLat = Encoding.UTF8.GetBytes(str);
            byte[] dataLen = BitConverter.GetBytes(sLat.Length);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(dataLen);
            }
            byte[] data = sLat;
            var serviceError = service.service_send(serviceClientHandle, dataLen, (uint)dataLen.Length, ref num2);
            if (serviceError != ServiceError.Success)
            {
                Console.WriteLine("send len:" + serviceError);
                return serviceError;
            }
            serviceError = service.service_send(serviceClientHandle, data, (uint)data.Length, ref num2);
            if (serviceError != ServiceError.Success)
            {
                Console.WriteLine("send data:" + serviceError);
                return serviceError;
            }
            return serviceError;
        }

        private static void Restore(ServiceClientHandle serviceClientHandle)
        {
            //还原
            Console.WriteLine("Restore");
            if (SendCmd(serviceClientHandle, 1) != ServiceError.Success)
            {
                Console.WriteLine("Restore failed");
                return;
            }
            Console.WriteLine("Restore success");
       }
        private static ServiceError SendCmd(ServiceClientHandle serviceClientHandle, uint cmd)
        {
            var service = iMobileDevice.LibiMobileDevice.Instance.Service;
            byte[] bytes = BitConverter.GetBytes(cmd);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            uint num = 0;
            Console.WriteLine("SendCmd: " + cmd);
            var serviceError = service.service_send(serviceClientHandle, bytes, (uint)bytes.Length, ref num);
            if (serviceError != ServiceError.Success)
            {
                Console.WriteLine("SendCmd: " + serviceError.ToString());
                return serviceError;
            }
            return serviceError;
        }

        private static Boolean RecoverDevice (string deviceId) {
            Console.WriteLine($"开始还原设备");

              var iDevice = iMobileDevice.LibiMobileDevice.Instance.iDevice;
            var lockdown = iMobileDevice.LibiMobileDevice.Instance.Lockdown;
            var service = iMobileDevice.LibiMobileDevice.Instance.Service;
            var mounter = iMobileDevice.LibiMobileDevice.Instance.MobileImageMounter;
            var num = 0u;
            iDevice.idevice_new(out var device, deviceId);
            var lockdowndError = lockdown.lockdownd_client_new_with_handshake(device, out LockdownClientHandle client, "com.alpha.jailout");//com.alpha.jailout
            lockdowndError = lockdown.lockdownd_start_service(client, "com.apple.dt.simulatelocation", out var service2);//com.apple.dt.simulatelocation
            var se = service.service_client_new(device, service2, out var client2);

            se = service.service_send(client2, new byte[4] { 0, 0, 0, 0 }, 4, ref num);
            se = service.service_send(client2, new byte[4] { 0, 0, 0, 1 }, 4, ref num);

            device.Dispose();
            client.Dispose();

            return true;
        }
    }
}