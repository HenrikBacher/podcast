plugins {
    kotlin("jvm") version "2.0.21"
    kotlin("plugin.serialization") version "2.0.21"
    id("org.graalvm.buildtools.native") version "0.9.28"
}

group = "dr1ommer"
version = "0.1"

val ktorVersion = "3.0.1"

allprojects {
    apply(plugin = "kotlin")

    repositories {
        mavenCentral()
    }

    dependencies {
        implementation("io.ktor:ktor-client-core:$ktorVersion")
        implementation("io.ktor:ktor-client-cio:$ktorVersion")
        implementation("io.ktor:ktor-client-content-negotiation:$ktorVersion")
        implementation("io.ktor:ktor-serialization-gson:$ktorVersion")
        implementation("io.ktor:ktor-client-logging:$ktorVersion")
        implementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.9.0")
        implementation("org.slf4j:slf4j-api:2.0.16")
        implementation("ch.qos.logback:logback-classic:1.5.12")
        implementation("com.google.code.gson:gson:2.11.0")
        implementation("org.jetbrains.kotlinx:kotlinx-cli:0.3.6")
    }

    tasks.test {
        useJUnitPlatform()
    }

    kotlin {
        jvmToolchain(21)
    }
}

graalvmNative {
    binaries {
        named("main") {
            imageName.set("ommer")
            mainClass.set("com.example.MainKt") // Replace with your actual main class
            debug.set(false)
            verbose.set(true)
            fallback.set(false)
        }
    }
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    compilerOptions {
        incremental = true
    }
}