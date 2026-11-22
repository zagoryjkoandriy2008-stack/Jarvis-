package com.andriy.jarvis;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.security.PrivateKey;
import java.security.Signature;
import java.security.spec.ECGenParameterSpec;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/** Pixel P-256 identity; private key never leaves Android Keystore. */
public final class JarvisDeviceIdentity {
    private static final String ALIAS="jarvis-device-v2-phone-p256";
    private JarvisDeviceIdentity(){}
    private static KeyStore store() throws Exception {
        KeyStore ks=KeyStore.getInstance("AndroidKeyStore");
        ks.load(null);
        return ks;
    }
    public static synchronized String publicKeyBase64() throws Exception {
        KeyStore ks=store();
        if(!ks.containsAlias(ALIAS)) {
            KeyPairGenerator gen=KeyPairGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_EC,"AndroidKeyStore");
            gen.initialize(new KeyGenParameterSpec.Builder(ALIAS,
                KeyProperties.PURPOSE_SIGN|KeyProperties.PURPOSE_VERIFY)
                .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256).build());
            gen.generateKeyPair();
            ks=store();
        }
        byte[] publicKey=ks.getCertificate(ALIAS).getPublicKey().getEncoded();
        return Base64.encodeToString(publicKey,Base64.NO_WRAP);
    }
    public static String fingerprint() throws Exception {
        byte[] hash=MessageDigest.getInstance("SHA-256").digest(
            publicKeyBase64().getBytes(StandardCharsets.UTF_8));
        StringBuilder sb=new StringBuilder();
        for(int i=0;i<8;i++)sb.append(String.format(java.util.Locale.ROOT,"%02X",hash[i]&0xff));
        return sb.toString();
    }
    public static Map<String,String> signedHeaders(String path,JSONObjectWrapper body)
        throws Exception {
        publicKeyBase64();
        if(!path.matches("^/device/v2/pair/(?:verify)$"))
            throw new IllegalArgumentException("Неприпустимий signed endpoint.");
        String timestamp=Long.toString(System.currentTimeMillis());
        String nonce=UUID.randomUUID().toString().replace("-","");
        byte[] hash=MessageDigest.getInstance("SHA-256").digest(
            body.raw.getBytes(StandardCharsets.UTF_8));
        StringBuilder hex=new StringBuilder();
        for(byte value:hash)hex.append(String.format(
            java.util.Locale.ROOT,"%02x",value&0xff));
        String canonical="POST\n"+path+"\nphone\n"+timestamp+"\n"+nonce+"\n"+hex;
        KeyStore ks=store();
        PrivateKey privateKey=(PrivateKey)ks.getKey(ALIAS,null);
        Signature signature=Signature.getInstance("SHA256withECDSA");
        signature.initSign(privateKey);
        signature.update(canonical.getBytes(StandardCharsets.UTF_8));
        byte[] encoded=EcdsaSignatureCodec.derToP1363(signature.sign());
        Map<String,String> headers=new HashMap<>();
        headers.put("X-Jarvis-Device","phone");
        headers.put("X-Jarvis-Timestamp",timestamp);
        headers.put("X-Jarvis-Nonce",nonce);
        headers.put("X-Jarvis-Signature",Base64.encodeToString(encoded,Base64.NO_WRAP));
        return headers;
    }
    public static final class JSONObjectWrapper {
        public final String raw;
        public JSONObjectWrapper(String raw){this.raw=raw;}
    }
}
