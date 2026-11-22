package com.andriy.jarvis;
/** Strict ECDSA P-256 DER signature to 64-byte WebCrypto P1363 adapter. */
public final class EcdsaSignatureCodec {
    private EcdsaSignatureCodec(){}
    public static byte[] derToP1363(byte[] der) {
        if(der==null || der.length<8 || der.length>74 ||
           (der[0]&255)!=0x30 || (der[1]&255)!=der.length-2)
            throw new IllegalArgumentException("Invalid P-256 DER ECDSA sequence.");
        int[] pos={2};
        byte[] raw=new byte[64];
        readInteger(der,pos,raw,0);
        readInteger(der,pos,raw,32);
        if(pos[0]!=der.length)
            throw new IllegalArgumentException("Unexpected DER trailer.");
        return raw;
    }
    private static void readInteger(byte[] der,int[] pos,byte[] result,int offset) {
        if(pos[0]+2>der.length || (der[pos[0]++]&255)!=2)
            throw new IllegalArgumentException("Malformed DER integer.");
        int length=der[pos[0]++]&255;
        if(length<1 || length>33 || pos[0]+length>der.length)
            throw new IllegalArgumentException("Invalid DER integer length.");
        int start=pos[0];
        if((der[start]&0x80)!=0)
            throw new IllegalArgumentException("Negative ECDSA integer.");
        if(length==33 && der[start]!=0)
            throw new IllegalArgumentException("Oversized ECDSA scalar.");
        if(length>1 && der[start]==0 && (der[start+1]&0x80)==0)
            throw new IllegalArgumentException("Non-canonical DER scalar.");
        if(length==33){start++;length--;}
        System.arraycopy(der,start,result,offset+32-length,length);
        pos[0]=start+length;
    }
}
