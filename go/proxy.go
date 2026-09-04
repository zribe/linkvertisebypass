package linkvertisebypass

import (
	"bytes"
	"context"
	cryptorand "crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"math/big"
	"net"
	"net/http"
	"net/url"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"
)

const (
	firstStickyPort = 10000
	lastStickyPort  = 20000
)

var (
	dataImpulseCountryPattern = regexp.MustCompile(`__cr\.[A-Za-z]{2}(?:,[A-Za-z]{2})*`)
	ipRoyalOptionPattern      = regexp.MustCompile(`_(?:country-[A-Za-z]{2}|session-[A-Za-z0-9]{8}|lifetime-[1-9][0-9]*[smhd]|forcerandom-1)`)
	countryPattern            = regexp.MustCompile(`^[a-z]{2}$`)
	lifetimePattern           = regexp.MustCompile(`^[1-9][0-9]*[smhd]$`)
)

type proxyConfig struct {
	provider        ProxyProvider
	server          string
	username        string
	password        string
	country         string
	sessionLifetime string
	port            int
	identity        string
}

type proxyProbeResult struct {
	proxy    proxyConfig
	snapshot probeData
	fresh    bool
}

func proxyFromOptions(options ProxyOptions) (*proxyConfig, error) {
	if !options.Enabled {
		return nil, nil
	}
	provider := options.Provider
	if provider == "" {
		provider = ProxyProviderDataImpulse
	}
	server := strings.TrimSpace(options.Server)
	switch provider {
	case ProxyProviderDataImpulse:
		if server == "" {
			server = "http://gw.dataimpulse.com:10000"
		}
	case ProxyProviderIPRoyal:
		if server == "" {
			server = "http://geo.iproyal.com:12321"
		}
	case ProxyProviderCustom:
		if server == "" {
			return nil, fmt.Errorf("custom proxy server is required")
		}
	default:
		return nil, fmt.Errorf("unsupported proxy provider %q", provider)
	}
	u, err := url.Parse(server)
	if err != nil || u.Hostname() == "" || (u.Scheme != "http" && u.Scheme != "https") {
		return nil, fmt.Errorf("invalid proxy server")
	}
	username := options.Username
	password := options.Password
	if u.User != nil {
		if username == "" {
			username = u.User.Username()
		}
		if password == "" {
			password, _ = u.User.Password()
		}
		u.User = nil
	}
	country := strings.ToLower(strings.TrimSpace(options.Country))
	if country != "" && !countryPattern.MatchString(country) {
		return nil, fmt.Errorf("proxy country must be a two-letter code")
	}
	lifetime := strings.ToLower(strings.TrimSpace(options.SessionLifetime))
	if lifetime == "" {
		lifetime = "10m"
	}
	if !lifetimePattern.MatchString(lifetime) {
		return nil, fmt.Errorf("IPRoyal session lifetime must look like 10m, 2h, or 30s")
	}
	if provider == ProxyProviderDataImpulse && country != "" {
		username = dataImpulseUsername(username, country)
	}
	port, _ := strconv.Atoi(u.Port())
	return &proxyConfig{provider: provider, server: u.Scheme + "://" + u.Host, username: username, password: password, country: country, sessionLifetime: lifetime, port: port}, nil
}

func (p proxyConfig) withPort(port int) proxyConfig {
	u, _ := url.Parse(p.server)
	u.Host = net.JoinHostPort(u.Hostname(), strconv.Itoa(port))
	p.server = u.Scheme + "://" + u.Host
	p.port = port
	p.identity = p.server
	return p
}

func (p proxyConfig) withIPRoyalSession() (proxyConfig, error) {
	session, err := randomSessionID()
	if err != nil {
		return proxyConfig{}, err
	}
	p.password = ipRoyalPassword(p.password, p.country, session, p.sessionLifetime)
	p.identity = session
	return p, nil
}

func (p proxyConfig) rotatable() bool {
	return p.provider == ProxyProviderIPRoyal || (p.provider == ProxyProviderDataImpulse && p.port >= firstStickyPort && p.port <= lastStickyPort)
}

func chooseFreshProxy(ctx context.Context, base proxyConfig, identifier map[string]any, canonical string, options ProxyOptions, used map[string]bool) (proxyConfig, probeData, int, error) {
	probes := 0
	for {
		candidates, err := proxyCandidates(base, options.ProbeBatch, used)
		if err != nil {
			return proxyConfig{}, probeData{}, probes, err
		}
		probeCtx, cancel := context.WithCancel(ctx)
		results := make(chan proxyProbeResult, len(candidates))
		var wait sync.WaitGroup
		for _, candidate := range candidates {
			wait.Add(1)
			go func(candidate proxyConfig) {
				defer wait.Done()
				snapshot, fresh := probeProxy(probeCtx, candidate, identifier, canonical, options.ProbeTimeout)
				results <- proxyProbeResult{candidate, snapshot, fresh}
			}(candidate)
		}
		go func() {
			wait.Wait()
			close(results)
		}()
		for result := range results {
			probes++
			if result.fresh {
				cancel()
				return result.proxy, result.snapshot, probes, nil
			}
		}
		cancel()
		if err := ctx.Err(); err != nil {
			return proxyConfig{}, probeData{}, probes, err
		}
	}
}

func proxyCandidates(base proxyConfig, count int, used map[string]bool) ([]proxyConfig, error) {
	result := make([]proxyConfig, 0, count)
	for len(result) < count {
		candidate := base
		var err error
		switch base.provider {
		case ProxyProviderDataImpulse:
			if len(used) > 0 {
				port, randomErr := randomPort()
				if randomErr != nil {
					return nil, randomErr
				}
				candidate = base.withPort(port)
			} else {
				candidate.identity = candidate.server
			}
		case ProxyProviderIPRoyal:
			candidate, err = base.withIPRoyalSession()
			if err != nil {
				return nil, err
			}
		}
		if used[candidate.identity] {
			continue
		}
		used[candidate.identity] = true
		result = append(result, candidate)
	}
	return result, nil
}

func probeProxy(ctx context.Context, proxy proxyConfig, identifier map[string]any, canonical string, timeout time.Duration) (probeData, bool) {
	proxyURL, err := url.Parse(proxy.server)
	if err != nil {
		return probeData{}, false
	}
	if proxy.username != "" {
		proxyURL.User = url.UserPassword(proxy.username, proxy.password)
	}
	transport := &http.Transport{Proxy: http.ProxyURL(proxyURL), TLSHandshakeTimeout: timeout, ResponseHeaderTimeout: timeout, DisableKeepAlives: true}
	defer transport.CloseIdleConnections()
	client := &http.Client{Transport: transport, Timeout: timeout}
	payload := graphQLRequest{OperationName: "probe", Query: probeQuery, Variables: map[string]any{
		"identifier": identifier,
		"task_args": map[string]any{"action_id": newActionID(), "additional_data": map[string]any{"taboola": map[string]any{
			"user_id": "fallbackUserId", "consent_string": "", "url": canonical, "external_referrer": "", "session_id": "",
		}}},
	}}
	body, _ := json.Marshal(payload)
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, graphQLEndpoint, bytes.NewReader(body))
	if err != nil {
		return probeData{}, false
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Origin", "https://linkvertise.com")
	req.Header.Set("Referer", canonical)
	req.Header.Set("User-Agent", browserUserAgent())
	response, err := client.Do(req)
	if err != nil {
		return probeData{}, false
	}
	defer response.Body.Close()
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return probeData{}, false
	}
	var envelope graphQLResponse[probeData]
	if err := json.NewDecoder(response.Body).Decode(&envelope); err != nil || len(envelope.Errors) > 0 {
		return probeData{}, false
	}
	return envelope.Data, contentIsFresh(envelope.Data.GetContent)
}

func contentIsFresh(content contentPayload) bool {
	if content.Typename == "DetailPageTargetData" {
		return true
	}
	for _, task := range content.Tasks {
		if task.Typename == "AdTask" || (task.Typename == "WaitTask" && (task.RemainingWaitingTime == nil || *task.RemainingWaitingTime <= 0)) {
			return true
		}
	}
	return false
}

func dataImpulseUsername(username, country string) string {
	tag := "__cr." + country
	if dataImpulseCountryPattern.MatchString(username) {
		return dataImpulseCountryPattern.ReplaceAllString(username, tag)
	}
	return username + tag
}

func ipRoyalPassword(password, country, session, lifetime string) string {
	base := ipRoyalOptionPattern.ReplaceAllString(password, "")
	if country != "" {
		base += "_country-" + country
	}
	return base + "_session-" + session + "_lifetime-" + lifetime
}

func randomPort() (int, error) {
	n, err := cryptorand.Int(cryptorand.Reader, big.NewInt(lastStickyPort-firstStickyPort+1))
	if err != nil {
		return 0, err
	}
	return firstStickyPort + int(n.Int64()), nil
}

func randomSessionID() (string, error) {
	value := make([]byte, 4)
	if _, err := cryptorand.Read(value); err != nil {
		return "", err
	}
	return hex.EncodeToString(value), nil
}
