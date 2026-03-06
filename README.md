# HtmlTransformer

A lightweight ASP.NET Core library for injecting HTML content and performing replacements in response streams.

## Features

- **HTML Injection**: Inject HTML at the start of `<head>`, end of `<head>`, or end of `<body>`.
- **Placeholders**: Replace custom placeholders in your HTML responses with dynamic content.
- **Efficient**: Uses a custom stream to perform injections and replacements on the fly without buffering the entire response (when possible).