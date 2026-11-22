package com.andriy.jarvis;
import java.security.*;
import java.security.spec.ECGenParameterSpec;
public final class JarvisP256SignatureContract {
    private static void require(boolean yes,String what) {
        if(!yes)throw new AssertionError(what);
    }
    public static void main(String[] args)throws Exception {
        KeyPairGenerator generator=KeyPairGenerator.getInstance("EC");
        generator.initialize(new ECGenParameterSpec("secp256r1"));
        KeyPair keys=generator.generateKeyPair();
        byte[] payload="POST\n/device/v2/pair/verify\nphone\n0000000000000\nnonce\nhash"
            .getBytes(java.nio.charset.StandardCharsets.UTF_8);
        Signature signer=Signature.getInstance("SHA256withECDSA");
        signer.initSign(keys.getPrivate());signer.update(payload);
        byte[] der=signer.sign();
        byte[] p1363=EcdsaSignatureCodec.derToP1363(der);
        require(p1363.length==64,"P1363 must be fixed 64 bytes");
        Signature verifier=Signature.getInstance("SHA256withECDSAinP1363Format");
        verifier.initVerify(keys.getPublic());verifier.update(payload);
        require(verifier.verify(p1363),"Java DER→P1363 verification");
        try{EcdsaSignatureCodec.derToP1363(new byte[]{0x30,0x01,0x00});
            throw new AssertionError("Invalid ASN.1 accepted");}
        catch(IllegalArgumentException expected){}
        System.out.println("PASS P-256 ECDSA signature contract");
    }
}
