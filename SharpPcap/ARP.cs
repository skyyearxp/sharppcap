/*
This file is part of SharpPcap.

SharpPcap is free software: you can redistribute it and/or modify
it under the terms of the GNU Lesser General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

SharpPcap is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public License
along with SharpPcap.  If not, see <http://www.gnu.org/licenses/>.
*/
/* 
 * Copyright 2005 Tamir Gal <tamir@tamirgal.com>
 * Copyright 2008-2009 Chris Morgan <chmorgan@gmail.com>
 */

using System;
using System.Net.NetworkInformation;

namespace SharpPcap
{
    /// <summary>
    /// Resolves MAC addresses from IP addresses using the Address Resolution Protocol (ARP)
    /// </summary>
    public class ARP
    {
        private string                  _deviceName;

        /// <summary>
        /// Constructs a new ARP Resolver
        /// </summary>
        public ARP()
        {
        }

        /// <summary>
        /// Constructs a new ARP Resolver
        /// </summary>
        /// <param name="deviceName">The name of the network device on which this resolver sends its ARP packets</param>
        public ARP(string deviceName)
        {
            DeviceName = deviceName;
        }

        /// <summary>
        /// The source MAC address to be used for ARP requests.
        /// If null, the local device MAC address is used
        /// </summary>
        public PhysicalAddress LocalMAC
        {
            get;
            set;
        }

        /// <summary>
        /// The source IP address to be used for ARP requests.
        /// If null, the local device IP address is used
        /// </summary>
        public System.Net.IPAddress LocalIP
        {
            get;
            set;
        }

        /// <summary>
        /// The default device name on which to send ARP requests
        /// </summary>
        public string DeviceName
        {
            get
            {
                return _deviceName;
            }
            set
            {
                _deviceName = value;
            }
        }

        /// <summary>
        /// Resolves the MAC address of the specified IP address. The 'DeviceName' propery must be set
        /// prior to using this method.
        /// </summary>
        /// <param name="destIP">The IP address to resolve</param>
        /// <returns>The MAC address that matches to the given IP address</returns>
        public PhysicalAddress Resolve(System.Net.IPAddress destIP)
        {
            if(DeviceName==null)
                throw new Exception("Can't resolve host: A network device must be specified");

            return Resolve(destIP, DeviceName);
        }

        /// <summary>
        /// Resolves the MAC address of the specified IP address
        /// </summary>
        /// <param name="destIP">The IP address to resolve</param>
        /// <param name="deviceName">The local network device name on which to send the ARP request</param>
        /// <returns>The MAC address that matches to the given IP address</returns>
        public PhysicalAddress Resolve(System.Net.IPAddress destIP, string deviceName)
        {
            PhysicalAddress localMAC = LocalMAC;
            System.Net.IPAddress localIP = LocalIP;
            //NetworkDevice device = new NetworkDevice(DeviceName);
            LivePcapDevice device = LivePcapDeviceList.Instance[DeviceName];

            //FIXME: PcapDevices don't have IpAddress
            //       These were present under Windows specific network adapters
            //       and may be present in pcap in the future with pcap-ng
            // if no local ip address is specified use the one from the
            // local device
#if false
            if(localIP == null)
                localIP = device.IpAddress;
#endif

            // if no local mac address is specified use the one from the device
            if(LocalMAC == null)
                localMAC = device.Interface.MacAddress;

            //Build a new ARP request packet
            var request = BuildRequest(destIP, localMAC, localIP);

            //create a "tcpdump" filter for allowing only arp replies to be read
            String arpFilter = "arp and ether dst " + localMAC.ToString();

            //open the device with 20ms timeout
            device.Open(DeviceMode.Promiscuous, 20);

            //set the filter
            device.Filter = arpFilter;

            //inject the packet to the wire
            device.SendPacket(request);

            PacketDotNet.ARPPacket arpPacket = null;

            while(true)
            {
                //read the next packet from the network
                var reply = device.GetNextPacket();
                if(reply == null)continue;

                // parse the packet
                var packet = PacketDotNet.Packet.ParsePacket(reply);

                // is this an arp packet?
                arpPacket = PacketDotNet.ARPPacket.GetEncapsulated(packet);
                if(arpPacket == null)
                {
                    continue;
                }

                //if this is the reply we're looking for, stop
                if(arpPacket.SenderProtocolAddress.Equals(destIP))
                {
                    break;
                }
            }

            //free the device
            device.Close();

            //return the resolved MAC address
            return arpPacket.SenderHardwareAddress;
        }

        private PacketDotNet.Packet BuildRequest(System.Net.IPAddress destinationIP,
                                                 PhysicalAddress localMac,
                                                 System.Net.IPAddress localIP)
        {
            // an arp packet is inside of an ethernet packet
            var ethernetPacket = new PacketDotNet.EthernetPacket(localMac,
                                                                 PhysicalAddress.Parse("FF-FF-FF-FF-FF-FF"),
                                                                 PacketDotNet.EthernetPacketType.Arp);

            var arpPacket = new PacketDotNet.ARPPacket(PacketDotNet.ARPOperation.Request,
                                                       PhysicalAddress.Parse("00-00-00-00-00-00"),
                                                       destinationIP,
                                                       localMac,
                                                       localIP);

            // the arp packet is the payload of the ethernet packet
            ethernetPacket.PayloadPacket = arpPacket;

            return ethernetPacket;
        }
    }
}