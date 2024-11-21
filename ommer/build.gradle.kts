plugins {
    kotlin("jvm")
    application
}

val mainClass = "ommer.client.ClientKt"

application {
    mainClass.set(mainClass)
}

tasks {
    jar {
        manifest { 
            attributes["Main-Class"] = mainClass 
        }
        duplicatesStrategy = DuplicatesStrategy.EXCLUDE
        
        // Include all runtime dependencies
        from(configurations.runtimeClasspath.get().map { 
            if (it.isDirectory) it else zipTree(it) 
        })
    }

    withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
        compilerOptions {
            incremental = true
        }
    }
}