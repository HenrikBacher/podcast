plugins { id("com.gradleup.shadow") version "8.3.5" }

val mainClass = "ommer.client.ClientKt"

tasks.jar {
    duplicatesStrategy = DuplicatesStrategy.EXCLUDE
    manifest { attributes["Main-Class"] = mainClass }
    from(configurations.runtimeClasspath.get().map { if (it.isDirectory) it else zipTree(it) })
}

tasks.withType<com.gradleup.shadow> {
    archiveClassifier.set("")
    manifest { attributes["Main-Class"] = mainClass }
    minimize()
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    kotlinOptions {
        incremental = true
    }
}