package linkvertisebypass

import (
	"context"
	cryptorand "crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"net/url"
	"runtime"
	"strconv"
	"strings"
	"time"
)

const (
	graphQLEndpoint = "https://publisher.linkvertise.com/graphql"
	contentQuery    = `query getContent($identifier: PublicLinkIdentificationInput!, $task_args: TaskArgument) {
		getContent(input: $identifier, task_args: $task_args) {
			__typename
			... on ContentAccessTaskSet {
				tasks {
					__typename id
					... on PremiumTask { status }
					... on WaitTask { status remainingWaitingTime adsTotal }
					... on AdTask {
						status adIndex adsTotal
						payloadBag { taboola { session_id } }
						ads {
							completion_token countdown provider
							provider_additional_payload { taboola { available_event_url visible_event_url } }
						}
					}
				}
			}
			... on DetailPageTargetData { type url paste }
		}
	}`
	probeQuery = `query probe($identifier: PublicLinkIdentificationInput!, $task_args: TaskArgument) {
		linkByIdentifier(linkIdentificationInput: $identifier) { target_host isPublished }
		getContent(input: $identifier, task_args: $task_args) {
			__typename
			... on ContentAccessTaskSet {
				tasks {
					__typename id
					... on PremiumTask { status }
					... on WaitTask { status remainingWaitingTime adsTotal }
					... on AdTask {
						status adIndex adsTotal
						payloadBag { taboola { session_id } }
						ads {
							completion_token countdown provider
							provider_additional_payload { taboola { available_event_url visible_event_url } }
						}
					}
				}
			}
			... on DetailPageTargetData { type url paste }
		}
	}`
	completeTaskMutation = `mutation completeTask($identifier: PublicLinkIdentificationInput!, $task_id: String!, $task_args: TaskArgument) {
		completeTask(input: $identifier, task_id: $task_id, task_args: $task_args) {
			__typename id
			... on AdTask { status }
			... on PremiumTask { status }
			... on WaitTask { status remainingWaitingTime adsTotal }
		}
	}`
	metadataQuery = `query getLinkByIdentifier($identifier: PublicLinkIdentificationInput!) {
		linkByIdentifier(linkIdentificationInput: $identifier) { target_host isPublished }
	}`
)

var supportedHosts = map[string]bool{
	"linkvertise.com": true, "link-center.net": true, "link-target.net": true,
	"link-target.org": true, "link-to.net": true, "link-hub.net": true,
	"up-to-down.net": true, "direct-link.net": true, "direct-links.net": true,
	"direct-links.org": true,
}

type graphQLRequest struct {
	OperationName string         `json:"operationName"`
	Query         string         `json:"query"`
	Variables     map[string]any `json:"variables"`
}

type graphQLError struct {
	Message string `json:"message"`
}

type graphQLResponse[T any] struct {
	Data   T              `json:"data"`
	Errors []graphQLError `json:"errors"`
}

type contentData struct {
	GetContent contentPayload `json:"getContent"`
}

type linkMetadata struct {
	TargetHost  string `json:"target_host"`
	IsPublished bool   `json:"isPublished"`
}

type probeData struct {
	Link       linkMetadata   `json:"linkByIdentifier"`
	GetContent contentPayload `json:"getContent"`
}

type contentPayload struct {
	Typename string `json:"__typename"`
	Type     string `json:"type"`
	URL      string `json:"url"`
	Paste    string `json:"paste"`
	Tasks    []task `json:"tasks"`
}

type task struct {
	Typename             string `json:"__typename"`
	ID                   string `json:"id"`
	Status               string `json:"status"`
	RemainingWaitingTime *int   `json:"remainingWaitingTime"`
	Ads                  []ad   `json:"ads"`
	PayloadBag           struct {
		Taboola struct {
			SessionID string `json:"session_id"`
		} `json:"taboola"`
	} `json:"payloadBag"`
}

type ad struct {
	CompletionToken string `json:"completion_token"`
	Provider        string `json:"provider"`
	Additional      struct {
		Taboola struct {
			AvailableURL string `json:"available_event_url"`
			VisibleURL   string `json:"visible_event_url"`
		} `json:"taboola"`
	} `json:"provider_additional_payload"`
}

type metadataData struct {
	Link linkMetadata `json:"linkByIdentifier"`
}

type completionData struct {
	CompleteTask struct {
		Status               string `json:"status"`
		RemainingWaitingTime *int   `json:"remainingWaitingTime"`
	} `json:"completeTask"`
}

type retryableError struct {
	err error
}

func (e retryableError) Error() string { return e.err.Error() }
func (e retryableError) Unwrap() error { return e.err }

func resolve(ctx context.Context, rawURL string, options Options) (Response, error) {
	source, canonical, identifier, err := parseLink(rawURL)
	if err != nil {
		return Response{}, err
	}
	baseProxy, err := proxyFromOptions(options.Proxy)
	if err != nil {
		return Response{}, err
	}
	usedProxies := map[string]bool{}
	probes := 0
	attempts := 0

	for {
		selected := baseProxy
		var snapshot *probeData
		if baseProxy != nil && baseProxy.rotatable() {
			candidate, probe, count, selectErr := chooseFreshProxy(ctx, *baseProxy, identifier, canonical.String(), options.Proxy, usedProxies)
			probes += count
			if selectErr != nil {
				return Response{}, selectErr
			}
			selected = &candidate
			snapshot = &probe
		}

		attempts++
		response, resolveErr := resolveOnce(ctx, source, canonical, identifier, selected, snapshot, options)
		response.ProxyAttempts = attempts
		response.ProxyProbes = probes
		if selected != nil {
			response.ProxyProvider = string(selected.provider)
			response.ProxyPort = selected.port
		}
		if resolveErr == nil {
			return response, nil
		}
		if baseProxy == nil || !baseProxy.rotatable() || !isRetryable(resolveErr) {
			return response, resolveErr
		}
		if err := ctx.Err(); err != nil {
			return response, err
		}
	}
}

func resolveOnce(ctx context.Context, source, canonical *url.URL, identifier map[string]any, proxy *proxyConfig, snapshot *probeData, options Options) (Response, error) {
	expectedHost := ""
	var current contentData
	if snapshot != nil {
		if !snapshot.Link.IsPublished {
			return Response{}, fmt.Errorf("link is not published")
		}
		expectedHost = strings.TrimSuffix(strings.TrimSpace(snapshot.Link.TargetHost), ".")
		current.GetContent = snapshot.GetContent
		if current.GetContent.Typename == "DetailPageTargetData" {
			return targetResponse(source, canonical, current.GetContent, expectedHost)
		}
	}

	transport, requestID, err := newBrowserTransport(canonical.String(), proxy, options.AutoInstallBrowser)
	if err != nil {
		return Response{}, retryableError{err}
	}
	defer transport.close()

	userID := "fallbackUserId"
	sessionID := ""
	if snapshot == nil {
		metadataPayload := graphQLRequest{
			OperationName: "getLinkByIdentifier",
			Query:         metadataQuery,
			Variables:     map[string]any{"identifier": identifier},
		}
		initialArgs := makeTaskArgs(requestID, userID, sessionID, canonical.String(), "")
		contentPayload := graphQLRequest{
			OperationName: "getContent",
			Query:         contentQuery,
			Variables: map[string]any{
				"identifier": identifier,
				"task_args":  initialArgs,
			},
		}
		bootstrap, bootstrapErr := transport.bootstrap(metadataPayload, contentPayload, canonical.String())
		if bootstrapErr != nil {
			return Response{}, retryableError{bootstrapErr}
		}
		metadata, decodeErr := decodeGraphQL[metadataData]([]byte(bootstrap.Metadata))
		if decodeErr != nil {
			return Response{}, fmt.Errorf("decode metadata: %w", decodeErr)
		}
		if !metadata.Link.IsPublished {
			return Response{}, fmt.Errorf("link is not published")
		}
		expectedHost = strings.TrimSuffix(strings.TrimSpace(metadata.Link.TargetHost), ".")
		if bootstrap.UserID != "" {
			userID = bootstrap.UserID
		}
		current, decodeErr = decodeGraphQL[contentData]([]byte(bootstrap.Content))
		if decodeErr != nil {
			return Response{}, decodeErr
		}
	}

	for round := 0; round < 10; round++ {
		if err := ctx.Err(); err != nil {
			return Response{}, err
		}
		taskArgs := makeTaskArgs(requestID, userID, sessionID, canonical.String(), "")
		content := current.GetContent
		if content.Typename == "DetailPageTargetData" {
			return targetResponse(source, canonical, content, expectedHost)
		}
		if content.Typename != "ContentAccessTaskSet" {
			return Response{}, fmt.Errorf("unknown content response %q", content.Typename)
		}

		waitTask, adTask := selectTasks(content.Tasks)
		if waitTask != nil {
			completion, next, completeErr := transitionTask(transport, identifier, *waitTask, taskArgs, taskArgs, "", canonical.String())
			if completeErr != nil {
				return Response{}, completeErr
			}
			if completion.CompleteTask.Status != "DONE" {
				seconds := waitTask.RemainingWaitingTime
				if completion.CompleteTask.RemainingWaitingTime != nil {
					seconds = completion.CompleteTask.RemainingWaitingTime
				}
				if seconds != nil && *seconds > 0 {
					return Response{}, retryableError{fmt.Errorf("proxy cooldown for about %d seconds", *seconds)}
				}
				return Response{}, retryableError{fmt.Errorf("initial wait task was not released")}
			}
			current = next
			continue
		}
		if adTask != nil {
			sessionID = adTask.PayloadBag.Taboola.SessionID
			completionToken := ""
			if len(adTask.Ads) > 0 {
				offer := adTask.Ads[0]
				completionToken = offer.CompletionToken
				transport.sendEvents(offer.Additional.Taboola.AvailableURL, offer.Additional.Taboola.VisibleURL)
				if options.TaskDelay > 0 {
					select {
					case <-time.After(options.TaskDelay):
					case <-ctx.Done():
						return Response{}, ctx.Err()
					}
				}
			}
			completionArgs := makeTaskArgs(requestID, userID, sessionID, canonical.String(), completionToken)
			nextArgs := makeTaskArgs(requestID, userID, sessionID, canonical.String(), "")
			completion, next, completeErr := transitionTask(transport, identifier, *adTask, completionArgs, nextArgs, completionToken, canonical.String())
			if completeErr != nil {
				return Response{}, completeErr
			}
			if completion.CompleteTask.Status != "DONE" {
				return Response{}, fmt.Errorf("signed ad completion was rejected")
			}
			current = next
			continue
		}
		return Response{}, fmt.Errorf("no actionable access task")
	}
	return Response{}, fmt.Errorf("target was not returned after ten task rounds")
}

func transitionTask(transport *browserTransport, identifier map[string]any, task task, args, nextArgs map[string]any, token, referrer string) (completionData, contentData, error) {
	if token != "" {
		args["completion_token"] = token
	}
	requests := []graphQLRequest{
		{
			OperationName: "completeTask",
			Query:         completeTaskMutation,
			Variables: map[string]any{
				"identifier": identifier,
				"task_id":    task.ID,
				"task_args":  args,
			},
		},
		{
			OperationName: "getContent",
			Query:         contentQuery,
			Variables: map[string]any{
				"identifier": identifier,
				"task_args":  nextArgs,
			},
		},
	}
	raw, err := transport.graphQLBatch(requests, referrer)
	if err != nil {
		return completionData{}, contentData{}, retryableError{err}
	}
	completion, err := decodeGraphQL[completionData](raw[0])
	if err != nil {
		return completionData{}, contentData{}, err
	}
	next, err := decodeGraphQL[contentData](raw[1])
	if err != nil {
		return completionData{}, contentData{}, err
	}
	return completion, next, nil
}

func targetResponse(source, canonical *url.URL, content contentPayload, expectedHost string) (Response, error) {
	response := Response{SourceURL: source.String(), CanonicalURL: canonical.String()}
	if destination, valid := parseDestination(content.URL); valid {
		if expectedHost != "" && !hostMatches(destination.Hostname(), expectedHost) {
			return response, fmt.Errorf("target host %q does not match metadata host %q", destination.Hostname(), expectedHost)
		}
		response.Type = "url"
		response.Value = destination.String()
		response.VerifiedHost = expectedHost
		return response, nil
	}
	response.Type = "text"
	response.Value = content.Paste
	return response, nil
}

func makeTaskArgs(requestID, userID, sessionID, canonical, completionToken string) map[string]any {
	args := map[string]any{
		"request_id": requestID,
		"action_id":  newActionID(),
		"additional_data": map[string]any{
			"taboola": map[string]any{
				"user_id": userID, "consent_string": "", "url": canonical,
				"external_referrer": "", "session_id": sessionID,
			},
		},
	}
	if completionToken != "" {
		args["completion_token"] = completionToken
	}
	return args
}

func selectTasks(tasks []task) (*task, *task) {
	var waitTask, adTask *task
	for index := range tasks {
		switch tasks[index].Typename {
		case "WaitTask":
			waitTask = &tasks[index]
		case "AdTask":
			adTask = &tasks[index]
		}
	}
	return waitTask, adTask
}

func decodeGraphQL[T any](raw []byte) (T, error) {
	var envelope graphQLResponse[T]
	if err := json.Unmarshal(raw, &envelope); err != nil {
		return envelope.Data, err
	}
	if len(envelope.Errors) > 0 {
		messages := make([]string, 0, len(envelope.Errors))
		for _, item := range envelope.Errors {
			messages = append(messages, item.Message)
		}
		return envelope.Data, fmt.Errorf("GraphQL: %s", strings.Join(messages, "; "))
	}
	return envelope.Data, nil
}

func parseLink(raw string) (*url.URL, *url.URL, map[string]any, error) {
	source, err := url.Parse(strings.TrimSpace(raw))
	if err != nil || (source.Scheme != "http" && source.Scheme != "https") || !supportedHosts[strings.ToLower(source.Hostname())] {
		return nil, nil, nil, fmt.Errorf("unsupported Linkvertise URL")
	}
	parts := splitPath(source.EscapedPath())
	if len(parts) > 0 && strings.EqualFold(parts[0], "access") {
		parts = parts[1:]
	}
	if len(parts) < 2 {
		return nil, nil, nil, fmt.Errorf("unsupported Linkvertise path")
	}
	userID, err := url.PathUnescape(parts[0])
	if err != nil {
		return nil, nil, nil, err
	}
	if _, err := strconv.ParseUint(userID, 10, 64); err != nil {
		return nil, nil, nil, fmt.Errorf("invalid Linkvertise user ID")
	}

	canonical := &url.URL{Scheme: "https", Host: "linkvertise.com"}
	if len(parts) >= 3 && parts[1] == "random" && parts[2] == "dynamic" && source.Query().Get("r") != "" {
		hash := source.Query().Get("r")
		value := map[string]any{
			"user_id":               userID,
			"hash":                  hash,
			"originates_from_adfly": source.Query().Get("link_origin") == "adfly",
		}
		if version, parseErr := strconv.Atoi(source.Query().Get("v")); parseErr == nil {
			value["version"] = version
		}
		canonical.Path = "/" + userID + "/random/dynamic"
		query := url.Values{"r": []string{hash}}
		canonical.RawQuery = query.Encode()
		return source, canonical, map[string]any{"userIdAndHash": value}, nil
	}

	slug, err := url.PathUnescape(parts[1])
	if err != nil || slug == "" {
		return nil, nil, nil, fmt.Errorf("invalid Linkvertise slug")
	}
	canonical.Path = "/" + userID + "/" + slug
	return source, canonical, map[string]any{
		"userIdAndUrl": map[string]any{"user_id": userID, "url": slug},
	}, nil
}

func splitPath(path string) []string {
	items := strings.Split(strings.Trim(path, "/"), "/")
	result := items[:0]
	for _, item := range items {
		if item != "" {
			result = append(result, item)
		}
	}
	return result
}

func parseDestination(value string) (*url.URL, bool) {
	destination, err := url.Parse(strings.TrimSpace(value))
	return destination, err == nil && (destination.Scheme == "http" || destination.Scheme == "https") && destination.Host != ""
}

func hostMatches(actual, expected string) bool {
	actual = strings.ToLower(strings.TrimSuffix(actual, "."))
	expected = strings.ToLower(strings.TrimSuffix(expected, "."))
	return actual == expected || strings.HasSuffix(actual, "."+expected)
}

func newActionID() string {
	value := newUUID() + newUUID() + newUUID()
	if len(value) > 100 {
		return value[:100]
	}
	return value
}

func newUUID() string {
	buffer := make([]byte, 16)
	_, _ = cryptorand.Read(buffer)
	buffer[6] = buffer[6]&0x0f | 0x40
	buffer[8] = buffer[8]&0x3f | 0x80
	encoded := hex.EncodeToString(buffer)
	return encoded[:8] + "-" + encoded[8:12] + "-" + encoded[12:16] + "-" + encoded[16:20] + "-" + encoded[20:]
}

func isRetryable(err error) bool {
	var retryable retryableError
	if errors.As(err, &retryable) {
		return true
	}
	message := strings.ToLower(err.Error())
	return strings.Contains(message, "cooldown") || strings.Contains(message, "timeout") ||
		strings.Contains(message, "net::err_") || strings.Contains(message, "proxy") ||
		strings.Contains(message, "connection") || strings.Contains(message, "cheq")
}

func browserUserAgent() string {
	platform := "X11; Linux x86_64"
	switch runtime.GOOS {
	case "windows":
		platform = "Windows NT 10.0; Win64; x64"
	case "darwin":
		platform = "Macintosh; Intel Mac OS X 10_15_7"
	}
	return "Mozilla/5.0 (" + platform + ") AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36"
}
