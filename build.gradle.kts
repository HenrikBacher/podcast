plugins {
    kotlin("jvm") version "2.1.20"
    kotlin("plugin.serialization") version "2.1.20"
}

group = "dr1ommer"
version = "1.1.13"

val javaVersion = 21
val ktorVersion = "3.1.3"
val coroutinesVersion = "1.10.2"

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
        implementation("org.slf4j:slf4j-simple:2.0.17")
        implementation("ch.qos.logback:logback-classic:1.5.18")
        implementation("com.google.code.gson:gson:2.13.1")
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