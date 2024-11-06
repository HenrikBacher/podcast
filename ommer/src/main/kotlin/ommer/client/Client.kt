package ommer.client

import com.google.gson.Gson
import io.ktor.client.*
import io.ktor.client.engine.cio.*
import io.ktor.client.plugins.contentnegotiation.*
import io.ktor.client.plugins.logging.*
import io.ktor.client.request.*
import io.ktor.client.statement.*
import io.ktor.http.*
import io.ktor.serialization.gson.*
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.runBlocking
import java.io.File
import java.time.Duration
import java.time.ZoneId
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import kotlin.math.abs
import kotlinx.cli.*
import ommer.drapi.Episodes
import ommer.drapi.Item
import ommer.drapi.Show
import ommer.rss.Feed
import ommer.rss.FeedItem
import ommer.rss.generate
import org.slf4j.LoggerFactory
import io.ktor.http.isSuccess
import io.ktor.client.plugins.compression.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.withContext

private val log = LoggerFactory.getLogger("ommer")
private val gson = Gson()
private val client = HttpClient(CIO) {
    install(ContentNegotiation) {
        gson()
    }
    install(Logging) {
        level = LogLevel.INFO
    }
    install(ContentEncoding) {
        gzip()
        deflate()
    }
    engine {
        maxConnectionsCount = 100
        endpoint {
            maxConnectionsPerRoute = 100
            pipelineMaxSize = 20
            keepAliveTime = 5000
            connectTimeout = 5000
            connectAttempts = 3
        }
    }
}

private fun String.appendPath(suffix: String): String = if (endsWith("/")) "$this$suffix" else "$this/$suffix"

private operator fun File.div(relative: String): File = resolve(relative)

private suspend fun fetchEpisodes(
    baseUri: String,
    urn: String,
    apiKey: String,
): List<Item> = withContext(Dispatchers.IO) {
    val items = mutableListOf<Item>()
    var currentUri = "${baseUri.appendPath(urn)}/episodes?limit=256"
    val uris = mutableListOf<String>()
    
    // First collect all URIs
    while (true) {
        val response = client.get(currentUri) {
            header("x-apikey", apiKey)
            contentType(ContentType.Application.Json)
            header("Accept-Encoding", "gzip, deflate")
        }
        
        if (!response.status.isSuccess()) break
        
        val episodes = gson.fromJson(response.bodyAsText(), Episodes::class.java)
        uris.add(currentUri)
        
        episodes.next?.let { 
            currentUri = it
        } ?: break
    }
    
    // Then fetch all episodes in parallel
    items.addAll(uris.map { uri ->
        async {
            val response = client.get(uri) {
                header("x-apikey", apiKey)
                contentType(ContentType.Application.Json)
                header("Accept-Encoding", "gzip, deflate")
            }
            if (response.status.isSuccess()) {
                gson.fromJson(response.bodyAsText(), Episodes::class.java).items
            } else {
                emptyList()
            }
        }
    }.awaitAll().flatten())
    
    items
}

fun Duration.formatHMS(): String =
        String.format("%02d:%02d:%02d", toHours(), toMinutesPart(), toSecondsPart())

data class Podcast(
        val urn: String,
        val slug: String,
        val titleSuffix: String?,
        val descriptionSuffix: String?,
        val feedUrl: String?,
        val imageUrl: String?
)

fun main(args: Array<String>) = runBlocking(Dispatchers.Default) {
    val parser = ArgParser("ommer", strictSubcommandOptionsOrder = false)
    val slug by
            parser.option(ArgType.String, fullName = "slug", description = "Podcast slug")
                    .required()
    val urn by
            parser.option(
                            ArgType.String,
                            fullName = "urn",
                            description = "Podcast URN, id format used by dr"
                    )
                    .required()
    val imageUrl by
            parser.option(
                            ArgType.String,
                            fullName = "imageUrl",
                            description = "Podcast image URL, found in the rss feed on dr.dk/lyd"
                    )
                    .required()

    val apiKey by
            parser.option(ArgType.String, fullName = "apiKey", description = "API key for dr api")
                    .required()
    val baseUrl by
            parser.option(
                            ArgType.String,
                            fullName = "baseUrl",
                            description = "Base URL for hosting"
                    )
                    .required()

    parser.parse(args)

    val apiUri = "https://api.dr.dk/radio/v2"

    val feedUrl = "https://${baseUrl}/feeds/${slug}.xml"
    val outputDirectory = File("output")

    val podcast =
            Podcast(
                    urn = urn,
                    slug = slug,
                    titleSuffix = "(Reproduceret feed)",
                    descriptionSuffix = "",
                    feedUrl = feedUrl,
                    imageUrl = imageUrl
            )

    val rssDateTimeFormatter = DateTimeFormatter.ofPattern("EEE, dd MMM yyyy HH:mm:ss Z")
    
    val feedDirectory = outputDirectory
    feedDirectory.mkdirs()
    val feedFile = outputDirectory / "${podcast.slug}.xml"
    log.info("Processing podcast ${podcast.slug}. Target feed: $feedFile")
    
    val response = client.get("${apiUri}/series/${podcast.urn}") {
        header("x-apikey", apiKey)
    }
    
    withContext(Dispatchers.IO) {
        if (response.status.isSuccess()) {
            val showInfo = gson.fromJson(response.bodyAsText(), Show::class.java)
            val episodes = async { fetchEpisodes("$apiUri/series", podcast.urn, apiKey) }
            
            val feed = with(showInfo) {
                Feed(
                    link = presentationUrl,
                    title = "$title${podcast.titleSuffix?.let { s -> " $s" } ?: ""}",
                    description = "$description${podcast.descriptionSuffix?.let { s -> "\n$s" } ?: ""}",
                    email = "podcast@dr.dk",
                    lastBuildDate = ZonedDateTime.parse(latestEpisodeStartTime)
                            .withZoneSameInstant(ZoneId.of("Europe/Copenhagen"))
                            .format(rssDateTimeFormatter),
                    feedUrl = "${podcast.feedUrl}",
                    imageUrl = "${podcast.imageUrl}",
                    imageLink = presentationUrl,
                    items = episodes.await()
                        .mapNotNull { item ->
                            with(item) {
                                val audioAsset = audioAssets
                                        .filter { it.format == "mp3" }
                                        .minByOrNull { abs(it.bitrate - 192) }
                                        ?: run {
                                            log.warn("No audio asset for ${item.id} (${item.title})")
                                            return@mapNotNull null
                                        }
                                FeedItem(
                                        guid = productionNumber,
                                        link = presentationUrl,
                                        title = title,
                                        description = description,
                                        pubDate = ZonedDateTime.parse(publishTime)
                                                .withZoneSameInstant(ZoneId.of("Europe/Copenhagen"))
                                                .format(rssDateTimeFormatter),
                                        duration = Duration.of(durationMilliseconds, ChronoUnit.MILLIS)
                                                .formatHMS(),
                                        enclosureUrl = audioAsset.url,
                                        enclosureByteLength = audioAsset.fileSize,
                                )
                            }
                        }
                        .toList(),
                )
            }
            feed.generate(feedFile)
        } else {
            throw IllegalStateException("Failed to fetch show info")
        }
    }
    client.close()
}
