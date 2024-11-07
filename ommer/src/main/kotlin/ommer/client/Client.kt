package ommer.client

// Ktor imports
import io.ktor.client.*
import io.ktor.client.engine.cio.*
import io.ktor.client.plugins.contentnegotiation.*
import io.ktor.client.request.*
import io.ktor.client.statement.*
import io.ktor.http.*
import io.ktor.http.isSuccess
import io.ktor.serialization.gson.*

// Kotlin standard & coroutines
import kotlinx.cli.*
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.retryWhen
import kotlinx.coroutines.flow.single
import kotlinx.coroutines.runBlocking

// Java imports
import java.io.File
import java.time.Duration
import java.time.ZoneId
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import kotlin.math.abs

// Project-specific imports
import com.google.gson.Gson
import org.slf4j.LoggerFactory
import ommer.drapi.Episodes
import ommer.drapi.Item
import ommer.drapi.Show
import ommer.rss.Feed
import ommer.rss.FeedItem
import ommer.rss.generate

private val log = LoggerFactory.getLogger("ommer")
private val gson = Gson()
private val client = HttpClient(CIO) {
    install(ContentNegotiation) {
        gson {
            // Optional: Configure Gson here if needed
        }
    }
}

private val maxRetries = 3
private val initialRetryDelay = 1000L // 1 second

private suspend fun <T> withRetry(
    operation: String,
    block: suspend () -> T
): T = flow {
    emit(block())
}.retryWhen { cause, attempt -> 
    if (attempt < maxRetries) {
        log.warn("$operation failed (attempt ${attempt + 1}/$maxRetries): ${cause.message}")
        delay(initialRetryDelay * (attempt + 1)) // Exponential backoff
        true
    } else {
        false
    }
}.single()

private fun String.appendPath(suffix: String): String = if (endsWith("/")) "$this$suffix" else "$this/$suffix"

private operator fun File.div(relative: String): File = resolve(relative)

private suspend fun fetchEpisodes(
    baseUri: String,
    urn: String,
    apiKey: String,
): List<Item> {
    val items = mutableListOf<Item>()
    var currentUri = "${baseUri.appendPath(urn)}/episodes?limit=256"
    
    while (true) {
        log.info("Getting $currentUri")
        val episodes = withRetry("Fetch episodes") {
            val response = client.get(currentUri) {
                header("x-apikey", apiKey)
                contentType(ContentType.Application.Json)
            }
            
            if (response.status.isSuccess()) {
                gson.fromJson(response.bodyAsText(), Episodes::class.java)
            } else {
                throw IllegalStateException("Failed to fetch episodes: ${response.status}")
            }
        }
        
        log.info("Got ${episodes.items.size} items")
        items.addAll(episodes.items)
        
        currentUri = episodes.next ?: break
    }
    
    return items
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

fun main(args: Array<String>) = runBlocking {
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
    
    val showInfo = withRetry("Fetch show info") {
        val response = client.get("${apiUri}/series/${podcast.urn}") {
            header("x-apikey", apiKey)
        }
        
        if (response.status.isSuccess()) {
            gson.fromJson(response.bodyAsText(), Show::class.java)
        } else {
            throw IllegalStateException("Failed to fetch show info: ${response.status}")
        }
    }
    
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
                items = fetchEpisodes("$apiUri/series", podcast.urn, apiKey)
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
    
    client.close()
}
