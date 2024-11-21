plugins { 
    id("com.github.johnrengelman.shadow") version "8.1.1" 
}

val mainClass = "ommer.client.ClientKt"

tasks {
    // Configure jar task
    jar {
        duplicatesStrategy = DuplicatesStrategy.EXCLUDE
        manifest { attributes["Main-Class"] = mainClass }
        from(configurations.runtimeClasspath.get().map { if (it.isDirectory) it else zipTree(it) })
    }

    // Configure shadowJar task
    withType<com.github.jengelman.gradle.plugins.shadow.tasks.ShadowJar> {
        archiveClassifier.set("")
        manifest { attributes["Main-Class"] = mainClass }
        minimize()
    }

    // Configure Kotlin compilation
    withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
        compilerOptions {
            incremental = true
        }
    }

    // Configure build task dependencies
    build {
        dependsOn(shadowJar)
    }
    
    // Configure assemble task dependencies 
    assemble {
        dependsOn(shadowJar)
    }
}