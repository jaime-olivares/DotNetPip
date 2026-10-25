from transformers import pipeline

# 1. Import Python AI library
# 2. Create the sentiment-analysis pipeline
clf = pipeline("sentiment-analysis")

# 3. Execute model inference
result = clf("Evaluating embedded AI performance in .NET 10")
print(result)
