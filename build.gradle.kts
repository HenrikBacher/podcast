plugins {
    kotlin("jvm") version "2.1.0"
    kotlin("plugin.serialization") version "2.1.0"
}

group = "dr1ommer"
version = "1.1.0"

val javaVersion = 21
val ktorVersion = "3.0.3"
val coroutinesVersion = "1.10.1"

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
        implementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:$coroutinesVersion")
        implementation("org.slf4j:slf4j-simple:2.0.16")
        implementation("ch.qos.logback:logback-classic:1.5.15")
        implementation("com.google.code.gson:gson:2.11.0")
        implementation("org.jetbrains.kotlinx:kotlinx-cli:0.3.6")
    }

    tasks.test {
        useJUnitPlatform()
    }

    kotlin {
        jvmToolchain(javaVersion)
    }
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    compilerOptions {
        incremental = true
    }
}