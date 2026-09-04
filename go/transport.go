package linkvertisebypass

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/url"
	"regexp"
	"strings"
	"time"

	http "github.com/bogdanfinn/fhttp"
	tlsclient "github.com/bogdanfinn/tls-client"
	"github.com/bogdanfinn/tls-client/profiles"
)

const (
	// The TLS and HTTP identity must match the embedded CHEQ browser fingerprint on every host OS.
	tlsUserAgent  = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
	tlsClientHint = `"Chromium";v="146", "Google Chrome";v="146", "Not_A Brand";v="99"`
)

var requestIDPattern = regexp.MustCompile(`"req"\s*:\s*"([^"]+)"`)

type tlsTransport struct {
	client    tlsclient.HttpClient
	context   context.Context
	canonical string
}

type bootstrapResult struct {
	Metadata string `json:"metadata"`
	Content  string `json:"content"`
	UserID   string `json:"userId"`
}

// newTLSTransport keeps collector, GraphQL, and event requests on one cookie and proxy session.
func newTLSTransport(ctx context.Context, canonical string, proxy *proxyConfig) (*tlsTransport, string, error) {
	options := []tlsclient.HttpClientOption{
		tlsclient.WithClientProfile(profiles.Chrome_146),
		tlsclient.WithCookieJar(tlsclient.NewCookieJar()),
		tlsclient.WithRandomTLSExtensionOrder(),
		tlsclient.WithDisableHttp3(),
		tlsclient.WithTimeoutSeconds(25),
	}
	if proxy != nil {
		proxyURL, err := authenticatedProxyURL(*proxy)
		if err != nil {
			return nil, "", err
		}
		options = append(options, tlsclient.WithProxyUrl(proxyURL))
	}
	client, err := tlsclient.NewHttpClient(tlsclient.NewNoopLogger(), options...)
	if err != nil {
		return nil, "", err
	}
	transport := &tlsTransport{client: client, context: ctx, canonical: canonical}
	collector, err := collectorURL(canonical)
	if err != nil {
		transport.close()
		return nil, "", err
	}
	body, err := transport.send(http.MethodGet, collector, nil, transport.scriptHeaders())
	if err != nil {
		transport.close()
		return nil, "", err
	}
	match := requestIDPattern.FindSubmatch(body)
	if len(match) != 2 {
		transport.close()
		return nil, "", fmt.Errorf("CHEQ returned no request ID")
	}
	return transport, string(match[1]), nil
}

func authenticatedProxyURL(proxy proxyConfig) (string, error) {
	value, err := url.Parse(proxy.server)
	if err != nil {
		return "", err
	}
	if proxy.username != "" {
		value.User = url.UserPassword(proxy.username, proxy.password)
	}
	return value.String(), nil
}

func (t *tlsTransport) close() {
	t.client.CloseIdleConnections()
}

func (t *tlsTransport) graphQLBatch(requests []graphQLRequest, referrer string) ([][]byte, error) {
	payload, err := json.Marshal(requests)
	if err != nil {
		return nil, err
	}
	body, err := t.send(http.MethodPost, graphQLEndpoint, payload, t.graphQLHeaders(referrer))
	if err != nil {
		return nil, err
	}
	var items []json.RawMessage
	if err := json.Unmarshal(body, &items); err != nil {
		return nil, err
	}
	if len(items) != len(requests) {
		return nil, fmt.Errorf("GraphQL batch returned %d of %d responses", len(items), len(requests))
	}
	result := make([][]byte, len(items))
	for index := range items {
		result[index] = items[index]
	}
	return result, nil
}

func (t *tlsTransport) bootstrap(metadata, content graphQLRequest, referrer string) (bootstrapResult, error) {
	items, err := t.graphQLBatch([]graphQLRequest{metadata, content}, referrer)
	if err != nil {
		return bootstrapResult{}, err
	}
	return bootstrapResult{Metadata: string(items[0]), Content: string(items[1]), UserID: "fallbackUserId"}, nil
}

func (t *tlsTransport) sendEvents(urls ...string) {
	for index, eventURL := range urls {
		parsed, err := url.Parse(eventURL)
		if err != nil || (parsed.Scheme != "http" && parsed.Scheme != "https") || parsed.Host == "" {
			continue
		}
		_, _ = t.send(http.MethodGet, eventURL, nil, t.eventHeaders())
		if index+1 < len(urls) {
			select {
			case <-time.After(50 * time.Millisecond):
			case <-t.context.Done():
				return
			}
		}
	}
}

func (t *tlsTransport) send(method, requestURL string, body []byte, headers http.Header) ([]byte, error) {
	var reader io.Reader
	if body != nil {
		reader = bytes.NewReader(body)
	}
	request, err := http.NewRequest(method, requestURL, reader)
	if err != nil {
		return nil, err
	}
	request = request.WithContext(t.context)
	request.Header = headers
	response, err := t.client.Do(request)
	if err != nil {
		return nil, err
	}
	defer response.Body.Close()
	responseBody, err := io.ReadAll(response.Body)
	if err != nil {
		return nil, err
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return nil, fmt.Errorf("HTTP %d: %s", response.StatusCode, strings.TrimSpace(string(responseBody)))
	}
	return responseBody, nil
}

func commonTLSHeaders() http.Header {
	return http.Header{
		"accept":             {"*/*"},
		"accept-language":    {"en-US,en;q=0.9"},
		"sec-ch-ua":          {tlsClientHint},
		"sec-ch-ua-mobile":   {"?0"},
		"sec-ch-ua-platform": {`"Windows"`},
		"user-agent":         {tlsUserAgent},
		http.HeaderOrderKey:  {"accept", "accept-language", "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform", "user-agent"},
	}
}

func (t *tlsTransport) scriptHeaders() http.Header {
	headers := commonTLSHeaders()
	headers["referer"] = []string{t.canonical}
	headers["sec-fetch-dest"] = []string{"script"}
	headers["sec-fetch-mode"] = []string{"no-cors"}
	headers["sec-fetch-site"] = []string{"cross-site"}
	headers[http.HeaderOrderKey] = append(headers[http.HeaderOrderKey], "referer", "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site")
	return headers
}

func (t *tlsTransport) graphQLHeaders(referrer string) http.Header {
	headers := commonTLSHeaders()
	headers["content-type"] = []string{"application/json"}
	headers["origin"] = []string{"https://linkvertise.com"}
	headers["referer"] = []string{referrer}
	headers["sec-fetch-dest"] = []string{"empty"}
	headers["sec-fetch-mode"] = []string{"cors"}
	headers["sec-fetch-site"] = []string{"same-site"}
	headers[http.HeaderOrderKey] = append(headers[http.HeaderOrderKey], "content-type", "origin", "referer", "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site")
	return headers
}

func (t *tlsTransport) eventHeaders() http.Header {
	headers := commonTLSHeaders()
	headers["referer"] = []string{t.canonical}
	headers["sec-fetch-dest"] = []string{"empty"}
	headers["sec-fetch-mode"] = []string{"cors"}
	headers["sec-fetch-site"] = []string{"cross-site"}
	headers[http.HeaderOrderKey] = append(headers[http.HeaderOrderKey], "referer", "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site")
	return headers
}
