// Plugins declarados a nivel raíz, aplicados en cada módulo.
plugins {
    // AGP 9 lleva el soporte de Kotlin dentro; ya no se declara el plugin
    // org.jetbrains.kotlin.android. https://kotl.in/gradle/agp-built-in-kotlin
    id("com.android.application") version "9.3.1" apply false
}
