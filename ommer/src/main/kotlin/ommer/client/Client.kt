package ommer.client

import com.google.gson.Gson
import java.io.File
import java.time.Duration
import java.time.ZoneId
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import kotlin.math.abs
import kotlinx.cli.*
import okhttp3.OkHttpClient
import okhttp3.Request
import ommer.drapi.Episodes
import ommer.drapi.Item
import ommer.drapi.Show
import ommer.rss.Feed
import ommer.rss.FeedItem
import ommer.rss.generate
import org.slf4j.LoggerFactory
import okhttp3.ConnectionSpec
import java.util.concurrent.TimeUnit

private val log = LoggerFactory.getLogger("ommer")
private val gson = Gson()

private fun String.appendPath(suffix: String): String = if (endsWith("/")) "$this$suffix" else "$this/$suffix"

private operator fun File.div(relative: String): File = resolve(relative)

private fun fetchEpisodes(
        client: OkHttpClient,
        baseUri: String,
        urn: String,
        apiKey: String,
): Sequence<Item> = sequence {
    var currentUri = "${baseUri.appendPath(urn)}/episodes?limit=256"
    var shouldContinue = true
    
    while (shouldContinue) {
        log.info("Getting $currentUri")
        val request = Request.Builder()
                .url(currentUri)
                .header("x-apikey", apiKey)
                .build()
        
        client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) {
                shouldContinue = false
                return@use
            }
            
            val episodes = gson.fromJson(response.body?.string(), Episodes::class.java)
            log.info("Got ${episodes.items.size} items")
            episodes.items.forEach { yield(it) }
            
            episodes.next?.let { 
                currentUri = it
            } ?: run {
                shouldContinue = false
            }
        }
    }
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

fun main(args: Array<String>) {
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
    val client = OkHttpClient.Builder()
            .connectionSpecs(listOf(ConnectionSpec.MODERN_TLS))
            .readTimeout(30, TimeUnit.SECONDS)
            .build()
    
    val feedDirectory = outputDirectory
    feedDirectory.mkdirs()
    val feedFile = outputDirectory / "${podcast.slug}.xml"
    log.info("Processing podcast ${podcast.slug}. Target feed: $feedFile")
    
    val request = Request.Builder()
            .url("${apiUri}/series/${podcast.urn}")
            .header("x-apikey", apiKey)
            .build()
    
    client.newCall(request).execute().use { response ->
        if (!response.isSuccessful) throw IllegalStateException("Failed to fetch show info")
        
        val showInfo = gson.fromJson(response.body?.string(), Show::class.java)
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
                    items = fetchEpisodes(client, "$apiUri/series", podcast.urn, apiKey)
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
    }
}
