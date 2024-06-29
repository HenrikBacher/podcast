package ommer.client

import ommer.drapi.Episodes
import ommer.drapi.Item
import ommer.drapi.Show
import ommer.rss.Feed
import ommer.rss.FeedItem
import ommer.rss.generate
import org.http4k.client.JettyClient
import org.http4k.core.Body
import org.http4k.core.HttpHandler
import org.http4k.core.Method.GET
import org.http4k.core.Request
import org.http4k.core.Uri
import org.http4k.core.query
import org.http4k.format.Gson.auto
import org.slf4j.LoggerFactory
import java.io.File
import java.io.FileInputStream
import java.time.Duration
import java.time.ZoneId
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.Properties
import kotlin.math.abs
import kotlinx.cli.*

private val log = LoggerFactory.getLogger("ommer")

private operator fun Uri.div(suffix: String): Uri = path("$path/$suffix")
private operator fun File.div(relative: String): File = resolve(relative)

private val episodes = Body.auto<Episodes>().toLens()
private val show = Body.auto<Show>().toLens()

private fun fetchEpisodes(
    client: HttpHandler,
    baseUri: Uri,
    urn: String,
    apiKey: String,
): Sequence<Item> = sequence {
    var currentUri = (baseUri / urn / "episodes").query("limit", "256")
    while (true) {
        log.info("Getting $currentUri")
        val response = episodes(client(Request(GET, currentUri).header("x-apikey", apiKey)))
        log.info("Got ${response.items.size} items")
        response.items.forEach { yield(it) }
        currentUri = response.next ?: break
    }
}

fun Duration.formatHMS(): String =
    String.format("%02d:%02d:%02d", toHours(), toMinutesPart(), toSecondsPart())

data class Podcast(val urn: String, val slug: String, val titleSuffix: String?, val descriptionSuffix: String?, val feedUrl: String?, val imageUrl: String?)

fun main(args: Array<String>) {  
    val parser = ArgParser("ommer", strictSubcommandOptionsOrder = false)
    val slug by parser.option(ArgType.String, fullName = "slug", description = "Podcast slug").required()
    val urn by parser.option(ArgType.String, fullName = "urn", description = "Podcast URN, id format used by dr").required()
    val imageUrl by parser.option(ArgType.String, fullName = "imageUrl", description = "Podcast image URL, found in the rss feed on dr.dk/lyd").required()

    val apiKey by parser.option(ArgType.String, fullName = "apiKey", description = "API key for dr api").required()
    val baseUrl by parser.option(ArgType.String, fullName = "baseUrl", description = "Base URL for hosting").required()

    parser.parse(args)

    val apiUri = Uri.of("https://api.dr.dk/radio/v2")

    val feedUrl = "https://${baseUrl}/feeds/${slug}.xml"
    val outputDirectory = File("output")

    val podcast = Podcast(
        urn = urn,
        slug = slug,
        titleSuffix = "(Reproduceret feed)",
        descriptionSuffix = "",
        feedUrl = feedUrl,
        imageUrl = imageUrl
    )
 
    val rssDateTimeFormatter = DateTimeFormatter.ofPattern("EEE, dd MMM yyyy HH:mm:ss Z")
    JettyClient().use { client ->
        val feedDirectory = outputDirectory 
        feedDirectory.mkdirs()
        val feedFile = outputDirectory / "${podcast.slug}.xml"
        log.info("Processing podcast ${podcast.slug}. Target feed: $feedFile")
        val response = client(Request(GET, apiUri / "series" / podcast.urn).header("x-apikey", apiKey))
        val showInfo = show(response)
        val feed = with(showInfo) {
            Feed(
                link = presentationUrl,
                title = "$title${podcast.titleSuffix?.let { s -> " $s" } ?: ""}",
                description = "$description${podcast.descriptionSuffix?.let { s -> "\n$s" } ?: ""}",
                email = "podcast@dr.dk",
                lastBuildDate = ZonedDateTime
                    .parse(latestEpisodeStartTime)
                    .withZoneSameInstant(ZoneId.of("Europe/Copenhagen"))
                    .format(rssDateTimeFormatter),
                feedUrl = "${podcast.feedUrl}",
                imageUrl = "${podcast.imageUrl}",
                imageLink = presentationUrl,
                items = fetchEpisodes(client, apiUri / "series", podcast.urn, apiKey).mapNotNull { item ->
                    with(item) {
                        val audioAsset = audioAssets
                            .filter { it.format == "mp3" }
                            // Select asset which is closest to bitrate 192
                            .minByOrNull { abs(it.bitrate - 192) } ?: run {
                            log.warn("No audio asset for ${item.id} (${item.title})")
                            return@mapNotNull null
                        }
                        FeedItem(
                            guid = productionNumber,
                            link = presentationUrl.toString(),
                            title = title,
                            description = description,
                            pubDate = ZonedDateTime
                                .parse(publishTime)
                                .withZoneSameInstant(ZoneId.of("Europe/Copenhagen"))
                                .format(rssDateTimeFormatter),
                            duration = Duration.of(durationMilliseconds, ChronoUnit.MILLIS).formatHMS(),
                            enclosureUrl = audioAsset.url.toString(),
                            enclosureByteLength = audioAsset.fileSize,
                        )
                    }
                }.toList(),
            )
        }
        feed.generate(feedFile)
    }
}
