package main

import (
	"bufio"
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"strings"
	"time"

	linkvertisebypass "github.com/zribe/linkvertisebypass/go"
)

func main() {
	install := flag.Bool("install-browser", false, "install the cross-platform Chromium runtime")
	timeout := flag.Duration("timeout", 3*time.Minute, "maximum resolution time")
	proxy := flag.String("proxy", "", "off, dataimpulse, iproyal, or custom")
	country := flag.String("country", "", "two-letter proxy country")
	flag.Parse()

	if *install {
		if err := linkvertisebypass.InstallBrowser(); err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		return
	}

	rawURL := ""
	if flag.NArg() > 0 {
		rawURL = flag.Arg(0)
	} else {
		fmt.Print("Linkvertise URL: ")
		line, _ := bufio.NewReader(os.Stdin).ReadString('\n')
		rawURL = strings.TrimSpace(line)
	}

	options := linkvertisebypass.OptionsFromEnvironment()
	options.Timeout = *timeout
	if *proxy != "" {
		options.Proxy.Enabled = *proxy != "off"
		if options.Proxy.Enabled {
			options.Proxy.Provider = linkvertisebypass.ProxyProvider(strings.ToLower(*proxy))
		}
	}
	if *country != "" {
		options.Proxy.Country = strings.ToLower(*country)
	}
	ctx, cancel := context.WithTimeout(context.Background(), *timeout)
	defer cancel()
	response, err := linkvertisebypass.BypassWithOptions(ctx, rawURL, options)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	encoder := json.NewEncoder(os.Stdout)
	encoder.SetIndent("", "  ")
	if err := encoder.Encode(response); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
