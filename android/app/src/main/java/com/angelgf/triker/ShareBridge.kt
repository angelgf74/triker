package com.angelgf.triker

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.util.Base64
import android.webkit.JavascriptInterface
import androidx.core.content.FileProvider
import java.io.File
import java.io.FileOutputStream

/**
 * Puente expuesto a la web como `window.AndroidShare`. Blazor lo invoca para
 * compartir la jugada obtenida (texto y, opcionalmente, una imagen PNG en
 * base64 con las cartas) a través de js/share.js (window.TrikerShare.share()).
 */
class ShareBridge(
    private val activity: Activity,
) {
    @JavascriptInterface
    fun share(text: String, imageBase64: String?) {
        // addJavascriptInterface ejecuta en un hilo propio; saltamos al hilo de UI.
        activity.runOnUiThread {
            val intent = if (imageBase64.isNullOrEmpty()) {
                Intent(Intent.ACTION_SEND).apply {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_TEXT, text)
                }
            } else {
                Intent(Intent.ACTION_SEND).apply {
                    type = "image/png"
                    putExtra(Intent.EXTRA_STREAM, writeImageToCache(imageBase64))
                    putExtra(Intent.EXTRA_TEXT, text)
                    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                }
            }
            activity.startActivity(Intent.createChooser(intent, null))
        }
    }

    // Las apps de terceros no pueden leer file:// directamente; hay que
    // exponer la imagen como content:// a través del FileProvider declarado
    // en AndroidManifest.xml (res/xml/file_paths.xml).
    private fun writeImageToCache(base64: String): Uri {
        val dir = File(activity.cacheDir, "images").apply { mkdirs() }
        val file = File(dir, "triker_jugada.png")
        FileOutputStream(file).use { it.write(Base64.decode(base64, Base64.DEFAULT)) }
        return FileProvider.getUriForFile(activity, "${activity.packageName}.fileprovider", file)
    }
}
