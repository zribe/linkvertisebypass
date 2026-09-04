package linkvertisebypass

import (
	"context"
	"os"
	"strconv"
	"strings"
	"time"

	"github.com/mxschmitt/playwright-go"
)

const (
	EnvProxyEnabled         = "LINKVERTISEBYPASS_PROXY_ENABLED"
	EnvProxyProvider        = "LINKVERTISEBYPASS_PROXY_PROVIDER"
	EnvProxyServer          = "LINKVERTISEBYPASS_PROXY_SERVER"
	EnvProxyUsername        = "LINKVERTISEBYPASS_PROXY_USERNAME"
	EnvProxyPassword        = "LINKVERTISEBYPASS_PROXY_PASSWORD"
	EnvProxyCountry         = "LINKVERTISEBYPASS_PROXY_COUNTRY"
	EnvProxySessionLifetime = "LINKVERTISEBYPASS_PROXY_SESSION_LIFETIME"
)

type ProxyProvider string

const (
	ProxyProviderDataImpulse ProxyProvider = "dataimpulse"
	ProxyProviderIPRoyal     ProxyProvider = "iproyal"
	ProxyProviderCustom      ProxyProvider = "custom"
)

type ProxyOptions struct {
	Enabled         bool
	Provider        ProxyProvider
	Server          string
	Username        string
	Password        string
	Country         string
	SessionLifetime string
	ProbeBatch      int
	ProbeTimeout    time.Duration
}

type Options struct {
	Proxy              ProxyOptions
	TaskDelay          time.Duration
	Timeout            time.Duration
	AutoInstallBrowser bool
}

type Response struct {
	SourceURL           string        `json:"sourceUrl"`
	CanonicalURL        string        `json:"canonicalUrl"`
	Type                string        `json:"type"`
	Value               string        `json:"value"`
	VerifiedHost        string        `json:"verifiedHost,omitempty"`
	ProxyProvider       string        `json:"proxyProvider,omitempty"`
	ProxyPort           int           `json:"proxyPort,omitempty"`
	ProxyProbes         int           `json:"proxyProbes"`
	ProxyAttempts       int           `json:"proxyAttempts"`
	Elapsed             time.Duration `json:"-"`
	ElapsedMilliseconds int64         `json:"elapsedMilliseconds"`
}

func OptionsFromEnvironment() Options {
	server := firstEnvironment(EnvProxyServer, "LINKVERTISEBYPASS_PROXY")
	username := os.Getenv(EnvProxyUsername)
	password := os.Getenv(EnvProxyPassword)
	configured := server != "" || username != "" || password != ""
	provider := ProxyProvider(strings.ToLower(strings.TrimSpace(os.Getenv(EnvProxyProvider))))
	if provider == "" {
		provider = ProxyProviderDataImpulse
	}
	return Options{
		Proxy: ProxyOptions{
			Enabled:         environmentBool(EnvProxyEnabled, configured),
			Provider:        provider,
			Server:          server,
			Username:        username,
			Password:        password,
			Country:         environmentDefault(EnvProxyCountry, "nl"),
			SessionLifetime: environmentDefault(EnvProxySessionLifetime, "10m"),
			ProbeBatch:      6,
			ProbeTimeout:    5 * time.Second,
		},
		TaskDelay:          50 * time.Millisecond,
		Timeout:            3 * time.Minute,
		AutoInstallBrowser: true,
	}
}

func Bypass(rawURL string) (Response, error) {
	return BypassContext(context.Background(), rawURL)
}

func BypassContext(ctx context.Context, rawURL string) (Response, error) {
	return BypassWithOptions(ctx, rawURL, OptionsFromEnvironment())
}

func BypassWith(rawURL string, options Options) (Response, error) {
	return BypassWithOptions(context.Background(), rawURL, options)
}

func DirectOptions() Options {
	options := OptionsFromEnvironment()
	options.Proxy.Enabled = false
	return options
}

func DataImpulseOptions(username, password, country string) Options {
	options := OptionsFromEnvironment()
	options.Proxy = ProxyOptions{
		Enabled: true, Provider: ProxyProviderDataImpulse,
		Username: username, Password: password, Country: country,
		SessionLifetime: "10m", ProbeBatch: 6, ProbeTimeout: 5 * time.Second,
	}
	return options
}

func IPRoyalOptions(username, password, country string) Options {
	options := OptionsFromEnvironment()
	options.Proxy = ProxyOptions{
		Enabled: true, Provider: ProxyProviderIPRoyal,
		Username: username, Password: password, Country: country,
		SessionLifetime: "10m", ProbeBatch: 6, ProbeTimeout: 5 * time.Second,
	}
	return options
}

func CustomProxyOptions(server, username, password string) Options {
	options := OptionsFromEnvironment()
	options.Proxy = ProxyOptions{
		Enabled: true, Provider: ProxyProviderCustom,
		Server: server, Username: username, Password: password,
		SessionLifetime: "10m", ProbeBatch: 6, ProbeTimeout: 5 * time.Second,
	}
	return options
}

func BypassWithOptions(ctx context.Context, rawURL string, options Options) (Response, error) {
	started := time.Now()
	if options.Proxy.ProbeBatch <= 0 {
		options.Proxy.ProbeBatch = 6
	}
	if options.Proxy.ProbeTimeout <= 0 {
		options.Proxy.ProbeTimeout = 5 * time.Second
	}
	if options.Timeout <= 0 {
		options.Timeout = 3 * time.Minute
	}

	if _, hasDeadline := ctx.Deadline(); !hasDeadline {
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(ctx, options.Timeout)
		defer cancel()
	}

	response, err := resolve(ctx, rawURL, options)
	response.Elapsed = time.Since(started)
	response.ElapsedMilliseconds = response.Elapsed.Milliseconds()
	return response, err
}

func InstallBrowser() error {
	return playwright.Install(&playwright.RunOptions{Browsers: []string{"chromium"}})
}

func firstEnvironment(names ...string) string {
	for _, name := range names {
		if value := strings.TrimSpace(os.Getenv(name)); value != "" {
			return value
		}
	}
	return ""
}

func environmentDefault(name, fallback string) string {
	if value := strings.TrimSpace(os.Getenv(name)); value != "" {
		return value
	}
	return fallback
}

func environmentBool(name string, fallback bool) bool {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return fallback
	}
	parsed, err := strconv.ParseBool(value)
	if err != nil {
		return fallback
	}
	return parsed
}
