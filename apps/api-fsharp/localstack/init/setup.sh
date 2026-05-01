#!/bin/bash
# LocalStack 起動時に自動実行される初期化スクリプト。
# Step 7 以降で使う SQS キュー、Step Functions ステートマシン、EventBridge ルールを用意する。

set -euo pipefail

REGION=ap-northeast-1

echo "=== Setting up LocalStack resources ==="

awslocal sqs create-queue --queue-name batch-notifications --region "$REGION"

# Step 8: CloudWatch Logs ロググループ - バッチアプリのログ集約先
awslocal logs create-log-group \
  --log-group-name /batch/sales-management \
  --region "$REGION"

# Step 7: Step Functions ステートマシン (薄いラッパー: 起動 → 結果記録 → 通知)
awslocal stepfunctions create-state-machine \
  --name batch-orchestrator \
  --definition file:///etc/localstack/init/ready.d/state-machine.json \
  --role-arn arn:aws:iam::000000000000:role/dummy \
  --region "$REGION"

STATE_MACHINE_ARN=$(awslocal stepfunctions list-state-machines \
  --region "$REGION" \
  --query 'stateMachines[?name==`batch-orchestrator`].stateMachineArn | [0]' \
  --output text)

# Step 7: EventBridge ルール - 毎月1日 0:00 (UTC) に月次締めバッチを実行
awslocal events put-rule \
  --name monthly-close-schedule \
  --schedule-expression "cron(0 0 1 * ? *)" \
  --region "$REGION"

awslocal events put-targets \
  --rule monthly-close-schedule \
  --targets "[{
    \"Id\": \"monthly-close\",
    \"Arn\": \"$STATE_MACHINE_ARN\",
    \"Input\": \"{\\\"jobName\\\":\\\"monthly-close\\\",\\\"jobParams\\\":\\\"$(date +%Y-%m)\\\"}\"
  }]" \
  --region "$REGION"

# Step 8: CloudWatch Alarm - エラー件数 > 0 で SQS に通知する
SQS_QUEUE_ARN="arn:aws:sqs:$REGION:000000000000:batch-notifications"

awslocal cloudwatch put-metric-alarm \
  --alarm-name batch-failure-alarm \
  --metric-name ErrorCount \
  --namespace BatchProcessing \
  --statistic Sum \
  --period 300 \
  --threshold 0 \
  --comparison-operator GreaterThanThreshold \
  --evaluation-periods 1 \
  --alarm-actions "$SQS_QUEUE_ARN" \
  --region "$REGION"

echo "=== LocalStack setup complete ==="
