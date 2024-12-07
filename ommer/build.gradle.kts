plugins { 
    id("com.gradleup.shadow") version "8.3.5" 
    id("com.guardsquare:proguard-gradle") version "7.5.0"
}

repositories {
    mavenCentral()
    gradlePluginPortal()
    maven("https://jitpack.io")
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
    minimize()
    exclude("META-INF/*.SF", "META-INF/*.DSA", "META-INF/*.RSA")
    exclude("META-INF/LICENSE*", "META-INF/NOTICE*", "META-INF/*.txt")
    exclude("META-INF/maven/**")
    exclude("META-INF/versions/**")
    exclude("module-info.class")
    mergeServiceFiles()
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    compilerOptions {
        incremental = true
        freeCompilerArgs = listOf("-opt-in=kotlin.RequiresOptIn", "-Xjvm-default=all")
    }
}

proguard {
    configurations {
        create("proguard") {
            configuration("proguard-rules.pro")
        }
    }
}