from http.server import HTTPServer, BaseHTTPRequestHandler
import requests

# Replace with a zero-rated domain (e.g., free.facebook.com)
ZERO_RATED_DOMAIN = "free.facebook.com"

class ZeroRatedProxy(BaseHTTPRequestHandler):
    def do_GET(self):
        try:
            # Forward request to zero-rated domain
            url = f"http://{ZERO_RATED_DOMAIN}{self.path}"
            response = requests.get(url, headers=self.headers)
            
            # Send response back to client
            self.send_response(response.status_code)
            for key, value in response.headers.items():
                self.send_header(key, value)
            self.end_headers()
            self.wfile.write(response.content)
        except Exception as e:
            self.send_error(500, str(e))

if __name__ == "__main__":
    server = HTTPServer(("0.0.0.0", 8080), ZeroRatedProxy)
    print("Zero-rated proxy running on port 8080")
    server.serve_forever()
      
