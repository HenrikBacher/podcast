plugins {
    kotlin("jvm") version "2.0.0"
}

group = "dr1ommer"
version = "0.1"

allprojects {
    apply(plugin = "kotlin")

    repositories {
        mavenCentral()
    }

    tasks.test {
        useJUnitPlatform()
    }

    kotlin {
        jvmToolchain(21)
    }
}
