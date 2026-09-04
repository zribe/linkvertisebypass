package linkvertisebypass

import (
	"encoding/json"
	"fmt"
	"strings"

	"github.com/mxschmitt/playwright-go"
)

const (
	cheqScript = "https://euob.bizseasky.com/sxp/i/df82c4ef6536e4dee60601280bc80588.js?id=14473"
)

type browserTransport struct {
	playwright *playwright.Playwright
	browser    playwright.Browser
	context    playwright.BrowserContext
	page       playwright.Page
}

type bootstrapResult struct {
	Metadata string `json:"metadata"`
	Content  string `json:"content"`
	UserID   string `json:"userId"`
}

func newBrowserTransport(canonical string, proxy *proxyConfig, autoInstall bool) (*browserTransport, string, error) {
	pw, err := playwright.Run()
	if err != nil && autoInstall {
		if installErr := InstallBrowser(); installErr != nil {
			return nil, "", fmt.Errorf("install browser: %w", installErr)
		}
		pw, err = playwright.Run()
	}
	if err != nil {
		return nil, "", fmt.Errorf("start Playwright: %w", err)
	}

	launch := playwright.BrowserTypeLaunchOptions{
		Headless: playwright.Bool(true),
		Args: []string{
			"--disable-background-networking",
			"--disable-blink-features=AutomationControlled",
			"--disable-component-update",
			"--disable-default-apps",
			"--disable-extensions",
			"--disable-sync",
			"--metrics-recording-only",
			"--mute-audio",
			"--no-first-run",
		},
	}
	if proxy != nil {
		launch.Proxy = &playwright.Proxy{Server: proxy.server}
		if proxy.username != "" {
			launch.Proxy.Username = playwright.String(proxy.username)
			launch.Proxy.Password = playwright.String(proxy.password)
		}
	}

	browser, err := pw.Chromium.Launch(launch)
	if err != nil && autoInstall && needsBrowserInstall(err) {
		_ = pw.Stop()
		if installErr := InstallBrowser(); installErr != nil {
			return nil, "", fmt.Errorf("install browser: %w", installErr)
		}
		pw, err = playwright.Run()
		if err == nil {
			browser, err = pw.Chromium.Launch(launch)
		}
	}
	if err != nil {
		_ = pw.Stop()
		return nil, "", fmt.Errorf("launch Chromium: %w", err)
	}

	context, err := browser.NewContext(playwright.BrowserNewContextOptions{
		Locale:    playwright.String("en-US"),
		UserAgent: playwright.String(browserUserAgent()),
	})
	if err != nil {
		_ = browser.Close()
		_ = pw.Stop()
		return nil, "", err
	}
	context.SetDefaultTimeout(15000)
	context.SetDefaultNavigationTimeout(15000)
	page, err := context.NewPage()
	if err != nil {
		_ = context.Close()
		_ = browser.Close()
		_ = pw.Stop()
		return nil, "", err
	}
	if err := page.Route(canonical, func(route playwright.Route) {
		_ = route.Fulfill(playwright.RouteFulfillOptions{
			Status:      playwright.Int(200),
			ContentType: playwright.String("text/html"),
			Body:        "<!doctype html><html><head></head><body></body></html>",
		})
	}); err != nil {
		_ = context.Close()
		_ = browser.Close()
		_ = pw.Stop()
		return nil, "", err
	}
	if _, err := page.Goto(canonical, playwright.PageGotoOptions{WaitUntil: playwright.WaitUntilStateDomcontentloaded}); err != nil {
		_ = context.Close()
		_ = browser.Close()
		_ = pw.Stop()
		return nil, "", err
	}

	requestIDValue, err := page.Evaluate(`src => new Promise((resolve, reject) => {
		const timer = setTimeout(() => reject(new Error("CHEQ timeout")), 15000);
		window.traffic_validation_cheq_response_ng_jsonp_0 = (_, requestId) => {
			clearTimeout(timer);
			resolve(requestId || "");
		};
		window.__ctcg_ct_14473_exec = undefined;
		const script = document.createElement("script");
		script.src = src;
		script.async = true;
		script.className = "ct_clicktrue_14473";
		script.setAttribute("data-ch", "cheq4ppc");
		script.setAttribute("data-jsonp", "traffic_validation_cheq_response_ng_jsonp_0");
		script.onerror = () => reject(new Error("CHEQ script failed"));
		document.head.appendChild(script);
	})`, cheqScript)
	if err != nil {
		_ = context.Close()
		_ = browser.Close()
		_ = pw.Stop()
		return nil, "", err
	}
	requestID, _ := requestIDValue.(string)
	if requestID == "" {
		_ = context.Close()
		_ = browser.Close()
		_ = pw.Stop()
		return nil, "", fmt.Errorf("CHEQ returned no request ID")
	}

	return &browserTransport{pw, browser, context, page}, requestID, nil
}

func (b *browserTransport) close() {
	_ = b.context.Close()
	_ = b.browser.Close()
	_ = b.playwright.Stop()
}

func (b *browserTransport) graphQL(operation, query string, variables map[string]any, referrer string) ([]byte, error) {
	payload := graphQLRequest{OperationName: operation, Query: query, Variables: variables}
	requestJSON, err := json.Marshal(payload)
	if err != nil {
		return nil, err
	}
	value, err := b.page.Evaluate(`async payload => {
		const response = await fetch("https://publisher.linkvertise.com/graphql", {
			method: "POST",
			credentials: "include",
			headers: {"accept": "*/*", "content-type": "application/json"},
			referrer: payload.referrer,
			body: payload.request
		});
		return JSON.stringify({status: response.status, body: await response.text()});
	}`, map[string]any{"request": string(requestJSON), "referrer": referrer})
	if err != nil {
		return nil, err
	}
	encoded, ok := value.(string)
	if !ok {
		return nil, fmt.Errorf("invalid browser response")
	}
	var response struct {
		Status int    `json:"status"`
		Body   string `json:"body"`
	}
	if err := json.Unmarshal([]byte(encoded), &response); err != nil {
		return nil, err
	}
	if response.Status < 200 || response.Status >= 300 {
		return nil, fmt.Errorf("GraphQL HTTP %d", response.Status)
	}
	return []byte(response.Body), nil
}

func (b *browserTransport) graphQLBatch(requests []graphQLRequest, referrer string) ([][]byte, error) {
	requestJSON, err := json.Marshal(requests)
	if err != nil {
		return nil, err
	}
	value, err := b.page.Evaluate(`async payload => {
		const response = await fetch("https://publisher.linkvertise.com/graphql", {
			method: "POST",
			credentials: "include",
			headers: {"accept": "*/*", "content-type": "application/json"},
			referrer: payload.referrer,
			body: payload.request
		});
		return JSON.stringify({status: response.status, body: await response.text()});
	}`, map[string]any{"request": string(requestJSON), "referrer": referrer})
	if err != nil {
		return nil, err
	}
	encoded, ok := value.(string)
	if !ok {
		return nil, fmt.Errorf("invalid browser response")
	}
	var response struct {
		Status int    `json:"status"`
		Body   string `json:"body"`
	}
	if err := json.Unmarshal([]byte(encoded), &response); err != nil {
		return nil, err
	}
	if response.Status < 200 || response.Status >= 300 {
		return nil, fmt.Errorf("GraphQL HTTP %d", response.Status)
	}
	var items []json.RawMessage
	if err := json.Unmarshal([]byte(response.Body), &items); err != nil {
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

func (b *browserTransport) bootstrap(metadataPayload, contentPayload graphQLRequest, referrer string) (bootstrapResult, error) {
	metadataJSON, err := json.Marshal(metadataPayload)
	if err != nil {
		return bootstrapResult{}, err
	}
	contentJSON, err := json.Marshal(contentPayload)
	if err != nil {
		return bootstrapResult{}, err
	}
	value, err := b.page.Evaluate(`async payload => {
		const post = body => fetch("https://publisher.linkvertise.com/graphql", {
			method: "POST", credentials: "include",
			headers: {"accept": "*/*", "content-type": "application/json"},
			referrer: payload.referrer, body
		}).then(response => response.text());
		const [metadata, content] = await Promise.all([post(payload.metadata), post(payload.content)]);
		return JSON.stringify({metadata, content, userId: "fallbackUserId"});
	}`, map[string]any{
		"metadata": string(metadataJSON),
		"content":  string(contentJSON),
		"referrer": referrer,
	})
	if err != nil {
		return bootstrapResult{}, err
	}
	encoded, ok := value.(string)
	if !ok {
		return bootstrapResult{}, fmt.Errorf("invalid bootstrap response")
	}
	var result bootstrapResult
	if err := json.Unmarshal([]byte(encoded), &result); err != nil {
		return bootstrapResult{}, err
	}
	return result, nil
}

func (b *browserTransport) sendEvents(urls ...string) {
	filtered := make([]string, 0, len(urls))
	for _, eventURL := range urls {
		if strings.HasPrefix(eventURL, "https://") || strings.HasPrefix(eventURL, "http://") {
			filtered = append(filtered, eventURL)
		}
	}
	if len(filtered) == 0 {
		return
	}
	_, _ = b.page.Evaluate(`urls => {
		for (const url of urls) fetch(url, {credentials: "include", keepalive: true}).catch(() => {});
		return true;
	}`, filtered)
}

func needsBrowserInstall(err error) bool {
	message := strings.ToLower(err.Error())
	return strings.Contains(message, "executable doesn't exist") || strings.Contains(message, "playwright install")
}
