package com.andriy.jarvis;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.util.Locale;

/** Pure local-LAN magic packet. Must never resolve untrusted hostnames. */
public final class WakeOnLanPacket {
    private WakeOnLanPacket(){}
    public static byte[] parseMac(String value) {
        if(value==null || !value.matches("(?i)^[0-9a-f]{2}(?::[0-9a-f]{2}){5}$"))
            throw new IllegalArgumentException("MAC повинен мати формат AA:BB:CC:DD:EE:FF");
        String[] parts=value.split(":");
        byte[] mac=new byte[6];
        for(int i=0;i<6;i++)mac[i]=(byte)Integer.parseInt(parts[i],16);
        boolean allZero=true,allOnes=true;
        for(byte b:mac){if(b!=0)allZero=false;if((b&0xff)!=255)allOnes=false;}
        if(allZero || allOnes || (mac[0]&1)!=0)
            throw new IllegalArgumentException("Потрібен індивідуальний unicast MAC.");
        return mac;
    }
    public static byte[] parseLocalBroadcast(String input) {
        if(input==null || !input.matches("^[0-9]{1,3}(?:\\.[0-9]{1,3}){3}$"))
            throw new IllegalArgumentException("Неправильна адреса локального broadcast.");
        String[] parts=input.split("\\.");
        byte[] ipv4=new byte[4];
        int[] v=new int[4];
        for(int i=0;i<4;i++) {
            v[i]=Integer.parseInt(parts[i]);
            if(v[i]>255)throw new IllegalArgumentException("Неправильний IPv4.");
            ipv4[i]=(byte)v[i];
        }
        boolean privateIp=v[0]==10 ||
            (v[0]==172 && v[1]>=16 && v[1]<=31) ||
            (v[0]==192 && v[1]==168);
        if(!privateIp || v[3]==0 || v[3]==1)
            throw new IllegalArgumentException("Потрібна приватна broadcast-адреса мережі.");
        return ipv4;
    }
    /** Validate broadcast against the Pixel's CURRENT IPv4 subnet, not just
     * against an arbitrary private IP on the internet/cafe network. */
    public static boolean matchesSubnet(byte[] phoneIp,int prefix,byte[] destination) {
        if(phoneIp==null || destination==null || phoneIp.length!=4 ||
           destination.length!=4 || prefix<8 || prefix>30)return false;
        int privateFirst=phoneIp[0]&255,privateSecond=phoneIp[1]&255;
        boolean privateIp=privateFirst==10 ||
            (privateFirst==172 && privateSecond>=16 && privateSecond<=31) ||
            (privateFirst==192 && privateSecond==168);
        if(!privateIp)return false;
        for(int i=0;i<4;i++) {
            int bits=Math.max(0,Math.min(8,prefix-i*8));
            int mask=bits==0?0:(0xff << (8-bits))&0xff;
            int expected=(phoneIp[i]&255)|(~mask&0xff);
            if(expected!=(destination[i]&255))return false;
        }
        return true;
    }
    public static byte[] build(String macAddress) {
        byte[] mac=parseMac(macAddress);
        byte[] packet=new byte[102];
        for(int i=0;i<6;i++)packet[i]=(byte)0xff;
        for(int i=0;i<16;i++)System.arraycopy(mac,0,packet,6+i*6,6);
        return packet;
    }
    public static boolean isWakeCommand(String spoken) {
        if(spoken==null || spoken.length()>100)return false;
        String phrase=spoken.toLowerCase(Locale.forLanguageTag("uk-UA")).trim()
            .replaceFirst("^(?:джарвіс|джарвис|jarvis)\\s+","");
        return phrase.matches("^(?:увімкни|включи|розбуди|ввімкни)\\s+(?:мій\\s+)?(?:комп'ютер|комп’ютер|компютер|пк|windows)$");
    }
}
