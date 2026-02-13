#!/usr/bin/env python3
"""Test Gemini Embedding API"""

import requests
import json
import sys

API_KEY = "Replace with your API Key"
MODEL = "models/text-embedding-004"
URL = f"https://generativelanguage.googleapis.com/v1/models/text-embedding-004:embedContent?key={API_KEY}"

payload = {
    "model": MODEL,
    "content": {
        "parts": [
            {"text": "Hello world"}
        ]
    }
}

try:
    response = requests.post(
        URL,
        headers={"Content-Type": "application/json"},
        json=payload,
        timeout=15
    )
    
    print(f"Status Code: {response.status_code}")
    print(f"Response: {response.text[:500]}")
    
    if response.status_code == 200:
        data = response.json()
        if "embedding" in data and "values" in data["embedding"]:
            print(f"? Embeddings working! Dimensions: {len(data['embedding']['values'])}")
        else:
            print(f"??  Response structure unexpected")
    else:
        print(f"? API Error")
        
except Exception as e:
    print(f"? Error: {e}")
