package main

import (
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

// 处理连接
func handleConnection(conn net.Conn) {
	fmt.Println("client connect ...")
	buffer := make([]byte, 256)
	for {
		n, err := conn.Read(buffer)
		if err != nil {
			fmt.Println("conn.Read ", err)
			return
		}
		fmt.Println(conn.RemoteAddr().String(), "receive data length : ", n)

		_, err = conn.Write(buffer[:n])
		if err != nil {
			fmt.Println("conn.Write ", err)
			return
		}
	}
}
