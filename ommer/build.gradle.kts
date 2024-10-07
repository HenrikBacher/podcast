plugins { id("com.github.johnrengelman.shadow") version "8.1.1" }

dependencies {
    implementation("org.slf4j:slf4j-api:2.0.16")
    implementation("ch.qos.logback:logback-classic:1.5.8")
    implementation("org.http4k:http4k-core:5.32.2.0")
    implementation("org.http4k:http4k-client-jetty:5.32.2.0")
    implementation("org.http4k:http4k-format-gson:5.32.3.0")
    implementation("org.jetbrains.kotlinx:kotlinx-cli:0.3.6")
}

val mainClass = "ommer.client.ClientKt"

tasks.jar {
    duplicatesStrategy = DuplicatesStrategy.EXCLUDE
    manifest { attributes["Main-Class"] = mainClass }
    from(configurations.runtimeClasspath.get().map { if (it.isDirectory) it else zipTree(it) })
}

tasks.withType<com.github.jengelman.gradle.plugins.shadow.tasks.ShadowJar> {
    archiveClassifier.set("")
    manifest { attributes["Main-Class"] = mainClass }
    minimize()
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    kotlinOptions {
        incremental = true
    }
}