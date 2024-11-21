plugins { id("com.github.johnrengelman.shadow") version "8.1.1" }

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
    compilerOptions {
        incremental = true
    }
}