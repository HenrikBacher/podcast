plugins {
    id("org.springframework.boot") version "3.2.0"
    id("io.spring.dependency-management") version "1.1.4"
}

val mainClass = "ommer.client.ClientKt"

tasks.bootJar {
    mainClass.set(mainClass)
    layered {
        enabled = true
        includeLayerTools = true
    }
}

configurations.all {
    exclude(group = "org.springframework.boot", module = "spring-boot-starter-logging")
    exclude(group = "org.springframework.boot", module = "spring-boot-starter-tomcat")
}

dependencies {
    implementation("org.springframework.boot:spring-boot-starter-web") {
        exclude(module = "spring-boot-starter-json")
        exclude(module = "jackson-databind")
    }
    // Use smaller logging framework
    implementation("org.slf4j:slf4j-simple")
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    kotlinOptions {
        incremental = true
        jvmTarget = "21"
    }
}