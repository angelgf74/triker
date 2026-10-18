plugins {
    // Desde AGP 9.0 el soporte de Kotlin viene integrado: aplicar ademas
    // org.jetbrains.kotlin.android falla. Ver https://kotl.in/gradle/agp-built-in-kotlin
    id("com.android.application")
}

// La release se compila sin firmar: la firma la pone "gplay compilar" con el
// keystore de ~/.google-play/keystores (ver playstore/gplay.json).
android {
    namespace = "com.angelgf.triker"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.angelgf.triker"
        minSdk = 26
        targetSdk = 37
        versionCode = 4
        versionName = "1.3"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

// DSL moderno de Kotlin: `android { kotlinOptions { jvmTarget } }` dejo de ser
// un aviso y pasa a error de compilacion del script en Kotlin 2.4.
kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.19.0")
    implementation("androidx.activity:activity-ktx:1.13.0")
    implementation("androidx.webkit:webkit:1.16.0")
    implementation("com.google.android.gms:play-services-ads:25.4.0")
    // play-services-ads arrastra androidx.fragment:1.1.0 (obsoleto, señalado por
    // Play Console); se fuerza la última versión estable.
    implementation("androidx.fragment:fragment:1.8.9")
}
