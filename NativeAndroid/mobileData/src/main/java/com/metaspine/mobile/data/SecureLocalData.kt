package com.metaspine.mobile.data

import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** AndroidKeyStore AES-GCM；旧明文只在成功加密并同步落盘后交给调用方。 */
internal object SecureLocalData {
    private const val ALIAS = "spineflow_mobile_local_v1"
    private const val PREFIX = "enc:v1:"

    @Synchronized
    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(
            KeyGenParameterSpec.Builder(
                ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            ).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return generator.generateKey()
    }

    fun seal(preferenceKey: String, plaintext: String): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        cipher.updateAAD(preferenceKey.toByteArray(Charsets.UTF_8))
        val iv = cipher.iv
        val encrypted = cipher.doFinal(plaintext.toByteArray(Charsets.UTF_8))
        return PREFIX + Base64.encodeToString(iv + encrypted, Base64.NO_WRAP)
    }

    fun read(preferences: SharedPreferences, preferenceKey: String): String? {
        val stored = preferences.getString(preferenceKey, null) ?: return null
        if (!stored.startsWith(PREFIX)) {
            require(!stored.startsWith("enc:")) { "不支持的本地加密格式" }
            check(preferences.edit().putString(preferenceKey, seal(preferenceKey, stored)).commit()) {
                "本地凭据加密迁移未能落盘"
            }
            return stored
        }
        return open(preferenceKey, stored)
    }

    /** Authenticated database payloads use the same Keystore key, with row identity as AAD. */
    fun open(preferenceKey: String, stored: String): String {
        require(stored.startsWith(PREFIX)) { "不支持的本地加密格式" }
        val bytes = Base64.decode(stored.removePrefix(PREFIX), Base64.NO_WRAP)
        require(bytes.size >= 12 + 16) { "本地加密数据已损坏" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes.copyOfRange(0, 12)))
        cipher.updateAAD(preferenceKey.toByteArray(Charsets.UTF_8))
        return cipher.doFinal(bytes.copyOfRange(12, bytes.size)).toString(Charsets.UTF_8)
    }
}
