package ommer.rss

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
        log.info("Creating XML document for feed: $title")
        val documentBuilder = DocumentBuilderFactory.newInstance().apply {
            isNamespaceAware = true
        }.newDocumentBuilder()
        val document = documentBuilder.newDocument()
        document.xmlStandalone = true
        
        log.debug("Building RSS feed structure")
        DSL(document).run {
            element("rss") {
                setAttribute("xmlns:atom", "http://www.w3.org/2005/Atom")
                setAttribute("xmlns:media", "http://search.yahoo.com/mrss/")
                setAttribute("xmlns:itunes", "http://www.itunes.com/dtds/podcast-1.0.dtd")
                setAttribute("version", "2.0")

                element("channel") {
                    element("atom:link") {
                        setAttribute("href", feedUrl)
                        setAttribute("rel", "self")
                        setAttribute("type", "application/rss+xml")
                    }
                    text("title", title)
                    text("link", link)
                    text("description", description)
                    text("language", language)
                    text("copyright", copyright)
                    text("managingEditor", email)
                    text("lastBuildDate", lastBuildDate)
                    text("itunes:explicit", if (explicit) "yes" else "no")
                    text("itunes:author", author)
                    text("itunes:block", "yes")
                    element("itunes:owner") {
                        text("itunes:email", email)
                        text("itunes:name", ownerName)
                    }
                    text("itunes:new-feed-url", feedUrl)
                    element("itunes:image") {
                        setAttribute("href", imageUrl)
                    }
                    element("image") {
                        text("url", imageUrl)
                        text("title", title)
                        text("link", link)
                    }
                    text("itunes:category", category)
                    text("media:restriction", mediaRestrictionCountry) {
                        setAttribute("type", "country")
                        setAttribute("relationship", "allow")
                    }
                    items.forEach { i ->
                        i.run {
                            element("item") {
                                text("guid", guid) { setAttribute("isPermalink", "false") }
                                text("link", link)
                                text("title", title)
                                text("description", description)
                                text("pubDate", pubDate)
                                text("explicit", if (explicit) "yes" else "no")
                                text("itunes:author", author)
                                text("itunes:duration", duration)
                                text("media:restriction", mediaRestrictionCountry) {
                                    setAttribute("type", "country")
                                    setAttribute("relationship", "allow")
                                }
                                element("enclosure") {
                                    setAttribute("url", enclosureUrl)
                                    setAttribute("type", "audio/mpeg")
                                    setAttribute("length", enclosureByteLength.toString())
                                }
                            }
                        }
                    }
                }
            }
        }
        
        log.debug("Configuring XML transformer")
        val transformer = TransformerFactory.newInstance().newTransformer().apply {
            setOutputProperty(OutputKeys.INDENT, "yes")
            setOutputProperty(OutputKeys.METHOD, "xml")
            setOutputProperty(OutputKeys.ENCODING, "UTF-8")
            setOutputProperty("{http://xml.apache.org/xslt}indent-amount", "2")
            setOutputProperty(OutputKeys.OMIT_XML_DECLARATION, "no")
            setOutputProperty(OutputKeys.STANDALONE, "yes")
        }
        
        log.info("Writing feed to file: ${feedFile.absolutePath}")
        val source = DOMSource(document)
        feedFile.bufferedWriter(Charsets.UTF_8, DEFAULT_BUFFER_SIZE).use { writer ->
            transformer.transform(source, StreamResult(writer))
        }
        log.info("Successfully wrote feed file")
        
    } catch (e: Exception) {
        log.error("Failed to generate RSS feed", e)
        throw IllegalStateException("Failed to generate RSS feed: ${e.message}", e)
    }
}
