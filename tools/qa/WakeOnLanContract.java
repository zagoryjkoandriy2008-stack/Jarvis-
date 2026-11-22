package com.andriy.jarvis;
import java.util.Arrays;
public final class WakeOnLanContract {
  private static void require(boolean ok,String label) {
    if(!ok)throw new AssertionError(label);
  }
  private static void invalid(Runnable r){
    try{r.run();throw new AssertionError("Accepted invalid input");}
    catch(IllegalArgumentException correct){}
  }
  public static void main(String[] args){
    byte[] packet=WakeOnLanPacket.build("02:11:22:33:44:55");
    require(packet.length==102,"102-byte packet");
    for(int i=0;i<6;i++)require((packet[i]&255)==255,"FF prefix");
    byte[] expected=new byte[]{2,17,34,51,68,85};
    for(int j=0;j<16;j++)
      require(Arrays.equals(Arrays.copyOfRange(packet,6+j*6,12+j*6),expected),
        "MAC repeated exactly");
    WakeOnLanPacket.parseLocalBroadcast("192.168.1.255");
    WakeOnLanPacket.parseLocalBroadcast("10.0.0.255");
    invalid(()->WakeOnLanPacket.parseLocalBroadcast("8.8.8.255"));
    invalid(()->WakeOnLanPacket.parseLocalBroadcast("127.0.0.255"));
    invalid(()->WakeOnLanPacket.parseLocalBroadcast("192.168.1.1"));
    invalid(()->WakeOnLanPacket.parseMac("ff:ff:ff:ff:ff:ff"));
    invalid(()->WakeOnLanPacket.parseMac("01:23:45:67:89:ab"));
    invalid(()->WakeOnLanPacket.parseMac("00:00:00:00:00:00"));
    require(WakeOnLanPacket.isWakeCommand("Джарвіс увімкни комп'ютер"),"UA wake");
    require(WakeOnLanPacket.isWakeCommand("розбуди ПК"),"wake synonym");
    require(!WakeOnLanPacket.isWakeCommand("відкрий спотіфай"),"no collision");
    System.out.println("PASS Wake-on-LAN pure Java contracts");
  }
}
