// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.realtime

import java.net.ServerSocket
import java.net.Socket
import java.security.MessageDigest
import java.util.Base64
import java.io.InputStream
import java.io.OutputStream

// A minimal RFC 6455 WebSocket server built on plain JDK sockets — deliberately hand-rolled rather than
// pulling in a Ktor server dependency, which the S035b task brief does not allow adding (jvmTest sources
// only). It exists so DashboardHubClientReconnectTest / AdminHubClientReconnectTest can drive the REAL
// DashboardHubClient/AdminHubClient over the REAL jvm HubSocket (Ktor CIO client) against a real accepted
// TCP connection with a genuine WS opening handshake — the only way to prove the reconnect/close behavior
// against actual wire traffic instead of asserting on internals.
internal class MiniWsServer : AutoCloseable {
    private val serverSocket: ServerSocket = ServerSocket(0)
    val port: Int get() = serverSocket.localPort

    /** Blocks until a client connects, completes the WS opening handshake, and returns the live [Connection]. */
    fun acceptConnection(): Connection {
        val socket: Socket = serverSocket.accept()
        val input: InputStream = socket.getInputStream()
        val output: OutputStream = socket.getOutputStream()
        performHandshake(input, output)
        return Connection(socket, input, output)
    }

    override fun close() {
        runCatching { serverSocket.close() }
    }

    private fun performHandshake(input: InputStream, output: OutputStream) {
        val requestLines: MutableList<String> = mutableListOf()
        val lineBuilder: StringBuilder = StringBuilder()
        while (true) {
            val byte: Int = input.read()
            if (byte == -1) break
            val char: Char = byte.toInt().toChar()
            if (char == '\n') {
                val line: String = lineBuilder.toString().trimEnd('\r')
                if (line.isEmpty()) break
                requestLines.add(line)
                lineBuilder.clear()
            } else {
                lineBuilder.append(char)
            }
        }
        val webSocketKey: String =
            requestLines
                .firstOrNull { it.startsWith("Sec-WebSocket-Key:", ignoreCase = true) }
                ?.substringAfter(":")
                ?.trim()
                ?: error("no Sec-WebSocket-Key header in test client handshake")

        val acceptSeed: String = webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
        val sha1: ByteArray = MessageDigest.getInstance("SHA-1").digest(acceptSeed.toByteArray(Charsets.UTF_8))
        val acceptValue: String = Base64.getEncoder().encodeToString(sha1)

        val response: String =
            "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: $acceptValue\r\n\r\n"
        output.write(response.toByteArray(Charsets.US_ASCII))
        output.flush()
    }

    /** One accepted, handshaken WS connection — send/receive text frames per RFC 6455. */
    internal class Connection(
        private val socket: Socket,
        private val input: InputStream,
        private val output: OutputStream,
    ) : AutoCloseable {

        /** Sends one unmasked text frame (server-to-client frames are never masked). */
        fun sendText(text: String) {
            val payload: ByteArray = text.toByteArray(Charsets.UTF_8)
            output.write(0x81) // FIN + text opcode
            writeLength(payload.size)
            output.write(payload)
            output.flush()
        }

        /** Sends a close frame (opcode 0x8) with no payload. */
        fun sendClose() {
            output.write(0x88)
            output.write(0x00)
            output.flush()
        }

        /** Reads the next text frame from the client, unmasking it. Returns null on EOF/close. */
        fun receiveText(): String? {
            val first: Int = input.read()
            if (first == -1) return null
            val opcode: Int = first and 0x0F
            val second: Int = input.read()
            if (second == -1) return null
            val masked: Boolean = (second and 0x80) != 0
            var length: Long = (second and 0x7F).toLong()
            if (length == 126L) {
                length = (readExact(2).let { ((it[0].toInt() and 0xFF) shl 8) or (it[1].toInt() and 0xFF) }).toLong()
            } else if (length == 127L) {
                val bytes: ByteArray = readExact(8)
                length = 0
                for (b: Byte in bytes) length = (length shl 8) or (b.toLong() and 0xFF)
            }
            val maskKey: ByteArray = if (masked) readExact(4) else ByteArray(0)
            val payload: ByteArray = readExact(length.toInt())
            if (masked) {
                for (i: Int in payload.indices) {
                    payload[i] = (payload[i].toInt() xor maskKey[i % 4].toInt()).toByte()
                }
            }
            if (opcode == 0x8) return null // client close frame
            return payload.toString(Charsets.UTF_8)
        }

        private fun readExact(count: Int): ByteArray {
            val buffer: ByteArray = ByteArray(count)
            var offset: Int = 0
            while (offset < count) {
                val read: Int = input.read(buffer, offset, count - offset)
                if (read == -1) error("test WS connection closed mid-frame")
                offset += read
            }
            return buffer
        }

        private fun writeLength(length: Int) {
            when {
                length < 126 -> output.write(length)
                length < 65536 -> {
                    output.write(126)
                    output.write((length shr 8) and 0xFF)
                    output.write(length and 0xFF)
                }
                else -> error("test server does not support frames >= 64KiB")
            }
        }

        override fun close() {
            runCatching { socket.close() }
        }
    }
}
