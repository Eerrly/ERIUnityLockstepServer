package main

import (
	"encoding/binary"
	"fmt"
	"net"

	kcp "github.com/xtaci/kcp-go"
)

type Client struct {
	lastRaw int32
	conn    net.Conn
}

var clientMap = make(map[int32]*Client)

func main() {
	if lis, err := kcp.Listen("127.0.0.1:10086"); err == nil {
		fmt.Println("waiting client ..")
		for {
			conn, err := lis.Accept()
			if err != nil {
				fmt.Println(err)
			}
			go handleConnection(conn)
		}
	}
}

// 处理连接
func handleConnection(conn net.Conn) {
	fmt.Println(conn.RemoteAddr().String(), " client connect ...")
	buffer := make([]byte, 6)
	lastRaw := int32(0)
	var playerId int32
	for {
		n, err := conn.Read(buffer)
		if err != nil {
			fmt.Println("conn.Read ", err)
			return
		}

		frame := int32(binary.LittleEndian.Uint32(buffer[:4]))
		raw := int32(binary.LittleEndian.Uint16(buffer[4:6]))
		playerId = raw & 1

		client, ok := clientMap[playerId]
		if !ok {
			client = &Client{lastRaw: lastRaw, conn: conn}
			clientMap[playerId] = client
		} else {
			client.lastRaw = lastRaw
		}

		if client.lastRaw != raw {
			client.lastRaw = raw
			fmt.Println("RecvData len:", n, "playerId : ", playerId, "frame:", frame, "raw:", raw)
		}

		writeData(buffer[:n])
	}
}

func writeData(data []byte) {
	for _, client := range clientMap {
		if client.conn != nil {
			_, err := client.conn.Write(data)
			if err != nil {
				fmt.Println("conn.Write ", err)
				client.conn.Close()
				delete(clientMap, client.lastRaw&1)
			}
		}
	}
}
