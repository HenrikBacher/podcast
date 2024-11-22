plugins { id("com.gradleup.shadow") version "8.3.5" }

// Enable build cache
buildCache {
    local {
        isEnabled = true
    }
}

tasks.named("build").configure {
    notCompatibleWithConfigurationCache("Build task")
}

tasks.named("assemble").configure {
    notCompatibleWithConfigurationCache("Assemble task")
}

val mainClass = "ommer.client.ClientKt"

tasks.jar {
    duplicatesStrategy = DuplicatesStrategy.EXCLUDE
    manifest { attributes["Main-Class"] = mainClass }
    from(configurations.runtimeClasspath.get().map { if (it.isDirectory) it else zipTree(it) })
}

tasks.withType<com.github.jengelman.gradle.plugins.shadow.tasks.ShadowJar> {
    archiveClassifier.set("")
    duplicatesStrategy = DuplicatesStrategy.EXCLUDE
    manifest { attributes["Main-Class"] = mainClass }
    mergeServiceFiles()
    minimize()
    exclude("META-INF/*.SF", "META-INF/*.DSA", "META-INF/*.RSA")
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    compilerOptions {
        incremental = true
        freeCompilerArgs = listOf("-opt-in=kotlin.RequiresOptIn", "-Xjvm-default=all")
    }
    outputs.cacheIf { true } // Enable caching for Kotlin compilation
}