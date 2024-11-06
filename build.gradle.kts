plugins {
    kotlin("jvm") version "2.0.21"
    kotlin("plugin.serialization") version "2.0.21"
}

group = "dr1ommer"
version = "0.1"

val ktorVersion = "2.3.12"

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
        implementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.9.0")
        implementation("io.ktor:ktor-client-caching:$ktorVersion")
        implementation("org.ehcache:ehcache:3.10.8")
    }

    tasks.test {
        useJUnitPlatform()
    }

    kotlin {
        jvmToolchain(21)
    }
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    compilerOptions {
        incremental = true
    }
}