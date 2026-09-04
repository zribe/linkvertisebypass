package main

import (
	"encoding/json"
	"os"

	linkvertisebypass "github.com/zribe/linkvertisebypass/go"
)

func main() {
	result, err := linkvertisebypass.Bypass(os.Args[1])
	if err != nil {
		panic(err)
	}
	encoder := json.NewEncoder(os.Stdout)
	encoder.SetIndent("", "  ")
	if err := encoder.Encode(result); err != nil {
		panic(err)
	}
}
