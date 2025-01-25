package ommer.rss

import com.rometools.rome.feed.module.ITunes
import com.rometools.rome.feed.synd.*
import com.rometools.rome.io.SyndFeedOutput
import org.slf4j.LoggerFactory
import java.io.File
import java.util.*

// Java standard
import java.io.File

// XML processing
import javax.xml.parsers.DocumentBuilderFactory
import javax.xml.transform.OutputKeys
import javax.xml.transform.TransformerFactory
import javax.xml.transform.dom.DOMSource
import javax.xml.transform.stream.StreamResult

// DOM
import org.w3c.dom.Document
import org.w3c.dom.Element
import org.w3c.dom.Node

import org.slf4j.LoggerFactory

data class Feed(
        val link: String,
        val title: String,
        val description: String,
        val language: String = "da",
        val copyright: String = "DR",
        val email: String,
        val lastBuildDate: String,
        val explicit: Boolean = false,
        val author: String = "DR",
        val ownerName: String = "DR",
        // atom:link, new-feed-url
        val feedUrl: String,
        val imageUrl: String,
        val imageLink: String,
        val category: String = "News",
        val mediaRestrictionCountry: String = "dk",
        val items: List<FeedItem>,
)

data class FeedItem(
        val guid: String,
        val link: String,
        val title: String,
        val description: String,
        val pubDate: String,
        val explicit: Boolean = false,
        val author: String = "DR",
        val duration: String,
        val mediaRestrictionCountry: String = "dk",
        val enclosureUrl: String,
        val enclosureByteLength: Long,
)

private class DSL(val document: Document) : Document by document {
    inline fun <A> Node.element(name: String, body: Element.() -> A): A {
        val element = createElement(name)
        appendChild(element)
        return body(element)
    }

    inline fun Node.text(name: String, contents: String, body: Element.() -> Unit = {}) =
            appendChild(
                    document.createElement(name)
                            .apply { appendChild(document.createTextNode(contents)) }
                            .also(body),
            )
}

private val log = LoggerFactory.getLogger(Feed::class.java)

fun Feed.generate(feedFile: File) {
    try {
        log.info("Creating RSS feed for: $title")
        val feed = SyndFeedImpl().apply {
            feedType = "rss_2.0"
            title = this@generate.title
            link = this@generate.link
            description = this@generate.description
            language = language
            copyright = copyright
            publishedDate = Calendar.getInstance().time
            modules = listOf(ITunes.Builder().apply {
                withAuthor(author)
                withImage(imageUrl)
                withExplicit(explicit)
                withOwnerEmail(email)
                withOwnerName(ownerName)
                withBlock(true)
                withNewFeedUrl(feedUrl)
            }.build())
            
            entries = items.map { item ->
                SyndEntryImpl().apply {
                    title = item.title
                    link = item.link
                    description = SyndContentImpl().apply { 
                        type = "text/plain"
                        value = item.description 
                    }
                    enclosures = listOf(SyndEnclosureImpl().apply {
                        url = item.enclosureUrl
                        type = "audio/mpeg"
                        length = item.enclosureByteLength
                    })
                    modules = listOf(ITunes.Builder().apply {
                        withAuthor(item.author)
                        withDuration(item.duration)
                        withExplicit(item.explicit)
                    }.build())
                }
            }
        }

        feedFile.bufferedWriter(Charsets.UTF_8).use { writer ->
            SyndFeedOutput().output(feed, writer)
        }
        log.info("Successfully wrote feed to: ${feedFile.absolutePath}")
        
    } catch (e: Exception) {
        log.error("Failed to generate RSS feed", e)
        throw IllegalStateException("Failed to generate RSS feed: ${e.message}", e)
    }
}
