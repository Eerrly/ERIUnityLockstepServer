package main

import (
	"encoding/binary"
	"fmt"
	"net"

	kcp "github.com/xtaci/kcp-go"
)

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

type Client struct {
	lastRaw int32
	conn    net.Conn
}

var clinet_arr [2]Client

// 处理连接
func handleConnection(conn net.Conn) {
	fmt.Println(conn.RemoteAddr().String(), " client connect ...")
	buffer := make([]byte, 4096)
	lastRaw := int32(0)
	for {
		n, err := conn.Read(buffer)
		if err != nil {
			fmt.Println("conn.Read ", err)
			return
		}

		playerId := int32(binary.LittleEndian.Uint32(buffer[:4]))
		frame := int32(binary.LittleEndian.Uint32(buffer[4:8]))
		raw := int32(binary.LittleEndian.Uint32(buffer[8:12]))

		if (clinet_arr[playerId] == Client{}) {
			clinet_arr[playerId] = Client{lastRaw: lastRaw, conn: conn}
		} else {
			clinet_arr[playerId].lastRaw = lastRaw
		}

		if clinet_arr[playerId].lastRaw != raw {
			clinet_arr[playerId].lastRaw = raw
			fmt.Println("RecvData len:", n, "playerId : ", playerId, "frame:", frame, "raw:", raw)
		}

		for _, client := range clinet_arr {
			if client.conn != nil {
				_, err = client.conn.Write(buffer[:n])
				if err != nil {
					fmt.Println("conn.Write ", err)
					return
				}
			}
		}
	}
}
